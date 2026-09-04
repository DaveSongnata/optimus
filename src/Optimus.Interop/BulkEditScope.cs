using System;

namespace Optimus.Interop
{
    /// <summary>
    /// Puts CorelDRAW into bulk-edit mode for the duration of an automated run, and — always — takes it
    /// back out.
    ///
    /// <para>
    /// Without this, CorelDRAW <b>repaints the canvas and fires events after every single edit</b>. On
    /// Davi's file that turned simplifying 14.342 curves into <b>13 minutes 22 seconds</b>: the work was
    /// not the geometry, it was 14.342 screen redraws nobody was looking at. Setting
    /// <c>Application.Optimization = true</c> and <c>EventsEnabled = false</c> is the documented way to
    /// suppress exactly that, and it is what every serious CorelDRAW automation does.
    /// </para>
    /// <para>
    /// Restoring is not optional and not best-effort-with-a-shrug: a session left with
    /// <c>Optimization = true</c> stops repainting, so CorelDRAW LOOKS frozen to the operator. The restore
    /// therefore runs from <see cref="Dispose"/>, remembers the previous values rather than assuming
    /// defaults, and forces one final repaint so the canvas shows the result.
    /// </para>
    /// <para>
    /// Both members are verified in the typelib: <c>Application.Optimization</c> (dump 3086) and
    /// <c>Application.EventsEnabled</c> (3064).
    /// </para>
    /// </summary>
    public sealed class BulkEditScope : IDisposable
    {
        private readonly dynamic _app;
        private readonly Action<string>? _log;

        private bool _previousOptimization;
        private bool _previousEvents = true;
        private bool _changedOptimization;
        private bool _changedEvents;

        public BulkEditScope(object application, Action<string>? log = null)
        {
            _app = application;
            _log = log;

            try
            {
                _previousOptimization = (bool)_app.Optimization;
                _app.Optimization = true;
                _changedOptimization = true;
            }
            catch (Exception ex) { _log?.Invoke("Optimization indisponível: " + ex.Message); }

            try
            {
                _previousEvents = (bool)_app.EventsEnabled;
                _app.EventsEnabled = false;
                _changedEvents = true;
            }
            catch (Exception ex) { _log?.Invoke("EventsEnabled indisponível: " + ex.Message); }
        }

        /// <summary>True when at least one of the two switches was actually taken.</summary>
        public bool Active => _changedOptimization || _changedEvents;

        public void Dispose()
        {
            // Events first: re-enabling them before the final repaint means the repaint is the one the
            // operator sees, not one Corel discards.
            if (_changedEvents)
            {
                try { _app.EventsEnabled = _previousEvents; }
                catch (Exception ex) { _log?.Invoke("FALHA ao religar EventsEnabled: " + ex.Message); }
                _changedEvents = false;
            }

            if (_changedOptimization)
            {
                try { _app.Optimization = _previousOptimization; }
                catch (Exception ex) { _log?.Invoke("FALHA CRÍTICA ao desligar Optimization: " + ex.Message); }
                _changedOptimization = false;
            }

            // One explicit repaint: with Optimization on, the canvas is stale by design, and leaving it
            // stale looks exactly like a crash.
            try { _app.ActiveWindow.Refresh(); } catch (Exception) { }
            try { _app.Refresh(); } catch (Exception) { }
        }
    }
}
