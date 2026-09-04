using System;

namespace Optimus.Interop
{
    /// <summary>
    /// Wraps every mutation of a document in one undo transaction, and GUARANTEES the transaction is
    /// closed — including when the operation throws.
    ///
    /// <para>
    /// An unpaired <c>BeginCommandGroup</c> corrupts CorelDRAW's undo/redo stack, and the documented
    /// recovery is for the user to close and reopen the file. That is not an acceptable outcome of an
    /// optimizer, so the pairing is enforced by <c>IDisposable</c> rather than left to a
    /// <c>finally</c> someone may forget.
    /// </para>
    /// </summary>
    public sealed class CommandGroupScope : IDisposable
    {
        private readonly dynamic _document;
        private readonly bool _opened;
        private bool _closed;

        public CommandGroupScope(dynamic document, string name)
        {
            _document = document;
            try
            {
                _document.BeginCommandGroup(name);
                _opened = true;
            }
            catch
            {
                // A document that refuses the group is still usable: the operation runs as individual
                // undo steps instead of one. Never abort the work over this.
                _opened = false;
            }
        }

        /// <summary>True when the undo transaction was actually opened.</summary>
        public bool IsOpen => _opened && !_closed;

        public void Dispose()
        {
            if (!_opened || _closed) return;
            _closed = true;
            try { _document.EndCommandGroup(); }
            catch { /* document already closed — nothing left to pair */ }
        }
    }
}
