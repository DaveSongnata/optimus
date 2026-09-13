namespace Optimus.AddIn
{
    /// <summary>
    /// Human-visible build tag. Logged at docker startup (%TEMP%\Optimus\docker.log) AND
    /// shown in the docker header, so we can instantly tell whether CorelDRAW loaded the new
    /// DLL or is still running a cached old one.
    /// </summary>
    internal static class Build
    {
        public const string Tag = "1.68.0";
    }
}
