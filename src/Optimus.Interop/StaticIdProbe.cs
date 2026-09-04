using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Optimus.Interop
{
    public sealed class StaticIdProbeResult
    {
        /// <summary>Ids taken from the file and looked up through COM.</summary>
        public int Tried { get; set; }

        /// <summary>Ids that found a live shape.</summary>
        public int Found { get; set; }

        /// <summary>StaticIDs read straight off live shapes, for comparison.</summary>
        public List<int> LiveSample { get; } = new List<int>();

        /// <summary>The file's ids used in the lookup, in the same order.</summary>
        public List<uint> FileSample { get; } = new List<uint>();

        public long ElapsedMs { get; set; }
        public string Note { get; set; } = "";

        /// <summary>
        /// True when the file's identifier reliably locates the live shape — the precondition for
        /// applying a disk-computed grouping through COM.
        /// </summary>
        public bool BridgeWorks => Tried > 0 && Found == Tried;
    }

    /// <summary>
    /// Tests one premise, cheaply: <b>is the id stored in the file the same as <c>Shape.StaticID</c>?</b>
    ///
    /// <para>
    /// It matters because it decides the whole repeated-art feature. The file census knows EXACTLY which
    /// shapes share art (byte-identical coordinate blocks) and every shape carries a unique id — measured
    /// on a real drawing: 16.914 shapes, 4.789 distinct geometries, zero duplicate ids. What is missing is
    /// the bridge to the live document, because late-bound <c>Curve.GetCurveInfo()</c> is refused by
    /// CorelDRAW 2024 ("the specified record cannot be mapped to a managed value class").
    /// </para>
    /// <para>
    /// If <c>Page.FindShape(StaticID:)</c> resolves those ids, the grouping never has to be recomputed
    /// through COM at all — and the expensive, unreliable coordinate route is simply not needed.
    /// </para>
    /// <para>
    /// Deliberately tiny: a couple of dozen lookups. A probe that costs minutes is a probe nobody runs.
    /// </para>
    /// </summary>
    public sealed class StaticIdProbe
    {
        private const int DefaultSample = 20;

        private readonly Action<string>? _log;

        public StaticIdProbe(Action<string>? log = null) => _log = log;

        /// <summary>
        /// <paramref name="fileIds"/> are the ids read from the container. <paramref name="page"/> is the
        /// live <c>Page</c>. Nothing is modified.
        /// </summary>
        public StaticIdProbeResult Run(object page, IList<uint> fileIds, int sample = DefaultSample)
        {
            var result = new StaticIdProbeResult();
            var clock = Stopwatch.StartNew();

            if (page == null || fileIds == null || fileIds.Count == 0)
            {
                result.Note = "sem página ou sem identificadores para testar";
                return result;
            }

            // First, what the live document says its ids are. If these look nothing like the file's, the
            // lookup below is pointless and the log shows why in one line.
            ReadLiveSample(page, result, sample);

            int step = Math.Max(1, fileIds.Count / sample);
            for (int i = 0; i < fileIds.Count && result.Tried < sample; i += step)
            {
                uint id = fileIds[i];
                if (id == 0) continue;

                result.Tried++;
                result.FileSample.Add(id);

                object? found = FindByStaticId(page, unchecked((int)id));
                if (found != null)
                {
                    result.Found++;
                    Release(found);
                }
            }

            clock.Stop();
            result.ElapsedMs = clock.ElapsedMilliseconds;

            if (result.Tried == 0) result.Note = "nenhum identificador utilizável no arquivo";
            else if (result.Found == 0)
                result.Note = "o identificador do arquivo NÃO corresponde ao StaticID do CorelDRAW";
            else if (result.Found < result.Tried)
                result.Note = $"apenas {result.Found} de {result.Tried} identificadores foram encontrados — "
                            + "a correspondência é parcial e não serve para alterar o arquivo";

            return result;
        }

        /// <summary>Reads StaticID off the first few live shapes, purely to show them side by side.</summary>
        private void ReadLiveSample(object page, StaticIdProbeResult result, int sample)
        {
            object? shapes = null;
            try
            {
                shapes = (object)((dynamic)page).Shapes;
                if (shapes == null) return;

                int count = (int)((dynamic)shapes).Count;
                int take = Math.Min(sample, count);

                for (int i = 1; i <= take; i++)
                {
                    object? shape = null;
                    try
                    {
                        shape = (object)((dynamic)shapes)[i];
                        if (shape == null) continue;
                        result.LiveSample.Add((int)((dynamic)shape).StaticID);
                    }
                    catch (Exception) { }
                    finally { Release(shape); }
                }
            }
            catch (Exception ex) { _log?.Invoke("StaticID ao vivo indisponível: " + ex.Message); }
            finally { Release(shapes); }
        }

        private object? FindByStaticId(object page, int staticId)
        {
            try
            {
                // Page.FindShape(Name?, Type?, StaticID?, Recursive?) — typelib 5392. Every parameter is
                // passed so the dispatch binder never has to fill an optional one; omitting a trailing
                // optional is what broke SaveAsCopy and ExportBitmap on this same build.
                return (object)((dynamic)page).FindShape(
                    Type.Missing, Type.Missing, staticId, true);
            }
            catch (Exception ex)
            {
                _log?.Invoke("FindShape(StaticID) falhou: " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        private static void Release(object? o)
        {
            try
            {
                if (o != null && System.Runtime.InteropServices.Marshal.IsComObject(o))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(o);
            }
            catch (Exception) { }
        }
    }
}
