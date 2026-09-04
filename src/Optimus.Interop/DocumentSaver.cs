using System;
using System.IO;
using Optimus.Core.Corel;
using Optimus.Core.Optimization;

namespace Optimus.Interop
{
    /// <summary>Thrown when the safety net could not be established. Never swallowed.</summary>
    public sealed class BackupFailedException : Exception
    {
        public BackupFailedException(string message) : base(message) { }
    }

    /// <summary>
    /// Writes the document back to disk with the operator's chosen save options, behind a verified
    /// backup.
    ///
    /// <para>
    /// Order matters and is not negotiable: the backup is created and VERIFIED first, and the whole
    /// optimization aborts if it cannot be — an irreversible operation without a safety net is not
    /// something to offer a paying customer (O2/P4).
    /// </para>
    /// <para>
    /// All save options are verified typelib members of <c>StructSaveAsOptions</c> (dump line 6720):
    /// <c>ThumbnailSize</c>, <c>IncludeCMXData</c>, <c>EmbedICCProfile</c>, <c>EmbedVBAProject</c>,
    /// <c>Version</c>. The instance comes from <c>Application.CreateStructSaveAsOptions()</c> (3112).
    /// </para>
    /// </summary>
    public sealed class DocumentSaver
    {
        private readonly dynamic _app;

        public DocumentSaver(object application) => _app = application;

        /// <summary>
        /// Whether the file being optimized ALREADY carried an embedded colour profile.
        ///
        /// <para>
        /// Set from the measured composition before saving. It exists so the colour slider can only ever
        /// preserve a profile, never introduce one — see the remark on <c>EmbedICCProfile</c> in
        /// <see cref="BuildOptions"/> for the 384 KB this cost on a real file.
        /// </para>
        /// </summary>
        public bool SourceHadColorProfile { get; set; }

        /// <summary>
        /// Creates the backup copy and verifies it exists with a plausible size. Returns its path.
        /// Throws <see cref="BackupFailedException"/> rather than proceeding unprotected.
        /// </summary>
        public string CreateVerifiedBackup(dynamic document, string originalPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
                throw new BackupFailedException(
                    "Salve o arquivo uma vez antes de otimizar — sem arquivo em disco não há como fazer cópia de segurança.");

            // Flush any unsaved edits first, so the copy on disk IS the operator's current work.
            // Without this the "backup" would silently be the last saved state.
            FlushIfDirty(document);

            long originalSize = new FileInfo(originalPath).Length;
            string dir = Path.GetDirectoryName(originalPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(originalPath);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupPath = Path.Combine(dir, $"{name}_backup_{stamp}.cdr");

            CopyToBackup(document, originalPath, backupPath);

            if (!File.Exists(backupPath))
                throw new BackupFailedException(
                    "A cópia de segurança não foi criada. A otimização foi cancelada.");

            // A backup a fraction of the original's size means the copy did not really happen.
            long backupSize = new FileInfo(backupPath).Length;
            if (backupSize < originalSize / 10)
                throw new BackupFailedException(
                    $"A cópia de segurança saiu suspeita ({backupSize:N0} bytes contra {originalSize:N0} do original). " +
                    "A otimização foi cancelada.");

            return backupPath;
        }

        /// <summary>
        /// Saves the document if it has unsaved changes, so the file on disk matches what is on screen.
        /// Best effort: a document that refuses to save still gets a backup of its last saved state, which
        /// is better than no backup at all.
        /// </summary>
        private static void FlushIfDirty(dynamic document)
        {
            try
            {
                if ((bool)document.Dirty) document.Save();
            }
            catch (Exception)
            {
                // Dirty/Save unavailable on this CorelDRAW build: proceed with what is on disk.
            }
        }

        /// <summary>
        /// Produces the backup file.
        ///
        /// <para>
        /// <b>A plain file copy, not <c>SaveAsCopy</c>.</b> On CorelDRAW 2024 the late-bound call
        /// <c>document.SaveAsCopy(path)</c> fails with "Could not convert argument 0" — the C# dynamic
        /// binder cannot satisfy a dispatch signature whose second parameter is an optional
        /// <c>StructSaveAsOptions</c>. Measured on Davi's VM: it aborted every optimization run.
        /// </para>
        /// <para>
        /// Copying the bytes is also the <i>better</i> backup regardless of that bug: <c>SaveAsCopy</c>
        /// RE-SERIALISES the document, so the "safety copy" would not be byte-identical to the file the
        /// operator has right now — and byte-identical is the entire point of a safety copy. It is
        /// faster, and it cannot fail inside COM.
        /// </para>
        /// <para>
        /// The COM path is kept only as a fallback for the case a file copy cannot work (the file locked
        /// by another process), invoked by reflection to bypass the dynamic binder that fails above.
        /// </para>
        /// </summary>
        private static void CopyToBackup(dynamic document, string originalPath, string backupPath)
        {
            try
            {
                File.Copy(originalPath, backupPath, overwrite: false);
                return;
            }
            catch (Exception copyEx)
            {
                try
                {
                    object doc = (object)document;
                    doc.GetType().InvokeMember(
                        "SaveAsCopy",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null, doc, new object[] { backupPath });
                    return;
                }
                catch (Exception comEx)
                {
                    throw new BackupFailedException(
                        "Não foi possível criar a cópia de segurança: " + copyEx.Message +
                        " (e o CorelDRAW também recusou: " + comEx.Message + ")." +
                        " A otimização foi cancelada para não arriscar seu arquivo.");
                }
            }
        }

        /// <summary>
        /// Saves over the same path with the options derived from the operator's sliders.
        /// </summary>
        public void SaveOptimized(dynamic document, string path, OptimizationSettings settings)
        {
            object? options = BuildOptions(settings);
            if (options == null)
            {
                document.Save();   // options unavailable: still save, just without the extra gains
                return;
            }

            try
            {
                document.SaveAs(path, options);
            }
            catch (Exception)
            {
                // Same dispatch-binder hazard that broke SaveAsCopy: retry through reflection, which
                // hands IDispatch the two arguments without the C# binder's optional-parameter metadata.
                // Falling back to a plain Save() keeps the operator's work safe even if this also fails —
                // they lose the extra save options, not the file.
                try
                {
                    object doc = (object)document;
                    doc.GetType().InvokeMember(
                        "SaveAs",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null, doc, new[] { (object)path, options });
                }
                catch (Exception)
                {
                    document.Save();
                }
            }
        }

        /// <summary>
        /// Builds <c>StructSaveAsOptions</c> from the settings. Returns null if the application will
        /// not create one, so the caller can fall back to a plain save.
        /// </summary>
        private object? BuildOptions(OptimizationSettings settings)
        {
            try
            {
                dynamic o = _app.CreateStructSaveAsOptions();

                // Free win: the embedded preview is only the thumbnail Explorer shows, and it is
                // STORED (incompressible) inside the container.
                if (settings.RemovePreview) o.ThumbnailSize = CorelConstants.CdrNoThumbnail;

                // Legacy CMX payload written for very old CorelDRAW versions.
                if (settings.RemoveCompatibilityData) o.IncludeCMXData = false;

                // The colour-fidelity slider. Dropping the profile is the biggest single lever on some
                // files (97.3% of one real file) and is NOT free — the caller has already told the
                // operator what it costs.
                //
                // CRITICAL: only ever KEEP a profile, never ADD one. Measured on kaneki_no_ponto.cdr,
                // which has no embedded profile at all: saving with EmbedICCProfile = true made CorelDRAW
                // write a 384 KB US Web Coated SWOP profile plus a 2,6 KB sRGB one into the output —
                // 5,4% of the file, handed back on a run that had just saved 19,7%. "High fidelity" on a
                // file with nothing to be faithful to is not fidelity, it is bloat, and it directly
                // contradicts what the docker already tells the operator ("este arquivo não tem perfil
                // de cor embutido").
                o.EmbedICCProfile = settings.EmbedColorProfile && SourceHadColorProfile;

                // A VBA project embedded in a drawing is dead weight for a print file.
                o.EmbedVBAProject = false;

                // Always the current format: writing an older version to save bytes would break the
                // customer's compatibility for a few percent.
                o.Version = CorelConstants.CdrCurrentVersion;

                return (object)o;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Closes and reopens the document so CorelDRAW reserializes it from scratch.
        ///
        /// <para>
        /// This is what drops retained undo state and orphaned payload — the fix for "the optimized
        /// file came out BIGGER", which Optimus v1.0 shipped. Returns the reopened document, or null
        /// when the round-trip failed (the file on disk is already saved either way).
        /// </para>
        /// </summary>
        public object? CloseAndReopen(string path)
        {
            try
            {
                dynamic current = _app.ActiveDocument;
                current.Close();
            }
            catch { /* already closed */ }

            try
            {
                dynamic reopened = _app.OpenDocument(path);
                // Save once more so the on-disk copy is the clean reserialization.
                try { reopened.Save(); } catch { }
                return (object)reopened;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Drops the retained undo stack before saving — the documented size-bloat fix.</summary>
        public static void ClearUndo(dynamic document)
        {
            try { document.ClearUndoList(); } catch { /* not supported / no undo */ }
        }
    }
}
