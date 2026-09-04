using System;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>
    /// Switches the active document to MILLIMETRES for the duration of an operation and restores
    /// the original unit on <see cref="Dispose"/> — always, including on error.
    ///
    /// <para>
    /// The unit matters more than it looks: every tolerance Optimus applies (node reduction,
    /// off-page detection, effective DPI) is expressed in millimetres, and CorelDRAW interprets
    /// those numbers in whatever unit the document is currently in. Optimus v1.0 set the document
    /// to <c>4</c> believing it was millimetres — <c>4</c> is <c>cdrCentimeter</c>, so every
    /// tolerance was applied 10x too aggressively.
    /// </para>
    /// </summary>
    public sealed class CorelDocumentState : IDisposable
    {
        private readonly dynamic _document;
        private readonly int _originalUnit;
        private bool _restored;

        public CorelDocumentState(dynamic document)
        {
            _document = document;
            _originalUnit = SafeGetUnit(document);
            try { _document.Unit = CorelConstants.CdrMillimeter; }
            catch { /* a document that refuses the unit is still usable; measurements degrade */ }
        }

        private static int SafeGetUnit(dynamic document)
        {
            try { return (int)document.Unit; }
            catch { return CorelConstants.CdrMillimeter; }
        }

        public void Dispose()
        {
            if (_restored) return;
            _restored = true;
            try { _document.Unit = _originalUnit; }
            catch { /* document already closed — nothing to restore */ }
        }
    }
}
