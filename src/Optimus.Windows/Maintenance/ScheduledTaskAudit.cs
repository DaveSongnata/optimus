using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Optimus.Windows.Maintenance
{
    /// <summary>
    /// The fifth startup surface: scheduled tasks that fire at logon.
    ///
    /// <para>
    /// This is the surface every "startup manager" forgets, and it is where modern software actually
    /// hides — updaters from Adobe, Google, Java and half the OEM utilities moved out of the Run keys years
    /// ago precisely because users clean those. A machine that looks clean in Task Manager can still be
    /// launching a dozen programs from here.
    /// </para>
    /// <para>
    /// Bound late through the <c>Schedule.Service</c> COM object by reflection: no interop assembly to
    /// ship, no dependency to merge, and it degrades to an empty list on a machine where the Task
    /// Scheduler service is unavailable. Disabling sets <c>Enabled = false</c> on the task definition,
    /// which is exactly what the Windows UI does and is fully reversible.
    /// </para>
    /// </summary>
    public static class ScheduledTaskAudit
    {
        /// <summary>TASK_TRIGGER_LOGON — the only trigger type this audit cares about.</summary>
        private const int TriggerLogon = 9;

        /// <summary>TASK_TRIGGER_BOOT — also runs without the operator asking for it.</summary>
        private const int TriggerBoot = 8;

        /// <summary>
        /// Microsoft's own tasks under <c>\Microsoft\Windows\</c> are skipped: they are Windows servicing
        /// itself (defender scans, update orchestration, disk cleanup), and offering to disable them would
        /// be offering to break the OS.
        /// </summary>
        private const string MicrosoftFolder = @"\Microsoft\";

        public static List<StartupEntry> All()
        {
            var entries = new List<StartupEntry>();
            object? service = null;

            try
            {
                Type? type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) return entries;

                service = Activator.CreateInstance(type);
                if (service == null) return entries;

                Invoke(service, "Connect", null, null, null, null);

                object? root = Invoke(service, "GetFolder", @"\");
                if (root == null) return entries;

                WalkFolder(root, entries, depth: 0);
            }
            catch (Exception)
            {
                // Task Scheduler unavailable or blocked by policy: this surface reports nothing rather
                // than failing the whole audit.
            }
            finally
            {
                try { if (service != null && Marshal.IsComObject(service)) Marshal.ReleaseComObject(service); }
                catch { }
            }

            return entries;
        }

        private static void WalkFolder(object folder, List<StartupEntry> entries, int depth)
        {
            if (depth > 8) return;

            try
            {
                object? tasks = Invoke(folder, "GetTasks", 0);
                if (tasks != null)
                {
                    int count = Convert.ToInt32(Get(tasks, "Count") ?? 0);
                    for (int i = 1; i <= count; i++)
                    {
                        object? task = null;
                        try
                        {
                            task = InvokeIndexer(tasks, i);
                            if (task == null) continue;
                            StartupEntry? entry = Describe(task);
                            if (entry != null) entries.Add(entry);
                        }
                        catch (Exception) { }
                        finally
                        {
                            try { if (task != null && Marshal.IsComObject(task)) Marshal.ReleaseComObject(task); }
                            catch { }
                        }
                    }
                    try { if (Marshal.IsComObject(tasks)) Marshal.ReleaseComObject(tasks); } catch { }
                }
            }
            catch (Exception) { }

            try
            {
                object? folders = Invoke(folder, "GetFolders", 0);
                if (folders == null) return;

                int count = Convert.ToInt32(Get(folders, "Count") ?? 0);
                for (int i = 1; i <= count; i++)
                {
                    object? sub = null;
                    try
                    {
                        sub = InvokeIndexer(folders, i);
                        if (sub == null) continue;

                        string path = Get(sub, "Path")?.ToString() ?? "";
                        if (path.StartsWith(MicrosoftFolder, StringComparison.OrdinalIgnoreCase)) continue;

                        WalkFolder(sub, entries, depth + 1);
                    }
                    catch (Exception) { }
                    finally
                    {
                        try { if (sub != null && Marshal.IsComObject(sub)) Marshal.ReleaseComObject(sub); }
                        catch { }
                    }
                }
                try { if (Marshal.IsComObject(folders)) Marshal.ReleaseComObject(folders); } catch { }
            }
            catch (Exception) { }
        }

        /// <summary>Returns an entry only for tasks that actually start something at logon or boot.</summary>
        private static StartupEntry? Describe(object task)
        {
            string path = Get(task, "Path")?.ToString() ?? "";
            if (path.StartsWith(MicrosoftFolder, StringComparison.OrdinalIgnoreCase)) return null;

            object? definition = Get(task, "Definition");
            if (definition == null) return null;

            try
            {
                if (!HasLogonTrigger(definition)) return null;

                string command = FirstExecAction(definition);
                if (string.IsNullOrWhiteSpace(command)) return null;

                bool enabled = true;
                try { enabled = Convert.ToBoolean(Get(task, "Enabled") ?? true); } catch (Exception) { }

                return new StartupEntry
                {
                    Name = Get(task, "Name")?.ToString() ?? path,
                    Command = command,
                    Surface = StartupSurface.ScheduledTask,
                    Location = path,
                    Enabled = enabled,
                    TargetMissing = !StartupAudit.ExecutableExists(command),
                };
            }
            finally
            {
                try { if (Marshal.IsComObject(definition)) Marshal.ReleaseComObject(definition); } catch { }
            }
        }

        private static bool HasLogonTrigger(object definition)
        {
            object? triggers = Get(definition, "Triggers");
            if (triggers == null) return false;

            try
            {
                int count = Convert.ToInt32(Get(triggers, "Count") ?? 0);
                for (int i = 1; i <= count; i++)
                {
                    object? trigger = null;
                    try
                    {
                        trigger = InvokeIndexer(triggers, i);
                        if (trigger == null) continue;
                        int type = Convert.ToInt32(Get(trigger, "Type") ?? -1);
                        if (type == TriggerLogon || type == TriggerBoot) return true;
                    }
                    catch (Exception) { }
                    finally
                    {
                        try { if (trigger != null && Marshal.IsComObject(trigger)) Marshal.ReleaseComObject(trigger); }
                        catch { }
                    }
                }
            }
            finally
            {
                try { if (Marshal.IsComObject(triggers)) Marshal.ReleaseComObject(triggers); } catch { }
            }

            return false;
        }

        private static string FirstExecAction(object definition)
        {
            object? actions = Get(definition, "Actions");
            if (actions == null) return "";

            try
            {
                int count = Convert.ToInt32(Get(actions, "Count") ?? 0);
                for (int i = 1; i <= count; i++)
                {
                    object? action = null;
                    try
                    {
                        action = InvokeIndexer(actions, i);
                        if (action == null) continue;

                        string? exe = Get(action, "Path")?.ToString();
                        if (string.IsNullOrWhiteSpace(exe)) continue;

                        string? args = null;
                        try { args = Get(action, "Arguments")?.ToString(); } catch (Exception) { }

                        return string.IsNullOrWhiteSpace(args) ? exe! : exe + " " + args;
                    }
                    catch (Exception) { }
                    finally
                    {
                        try { if (action != null && Marshal.IsComObject(action)) Marshal.ReleaseComObject(action); }
                        catch { }
                    }
                }
            }
            finally
            {
                try { if (Marshal.IsComObject(actions)) Marshal.ReleaseComObject(actions); } catch { }
            }

            return "";
        }

        /// <summary>
        /// Turns a scheduled task on or off by its registered path. Reversible by design: the task
        /// definition is left in place and only <c>Enabled</c> changes, exactly as the Windows UI does it.
        /// </summary>
        public static bool SetEnabled(string taskPath, bool enabled, out string message)
        {
            object? service = null;
            object? task = null;

            try
            {
                Type? type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) { message = "O Agendador de Tarefas não está disponível."; return false; }

                service = Activator.CreateInstance(type);
                if (service == null) { message = "O Agendador de Tarefas não respondeu."; return false; }

                Invoke(service, "Connect", null, null, null, null);

                string folderPath = taskPath.Substring(0, Math.Max(1, taskPath.LastIndexOf('\\')));
                if (folderPath.Length == 0) folderPath = @"\";

                object? folder = Invoke(service, "GetFolder", folderPath);
                if (folder == null) { message = "Pasta da tarefa não encontrada."; return false; }

                string name = taskPath.Substring(taskPath.LastIndexOf('\\') + 1);
                task = Invoke(folder, "GetTask", name);
                if (task == null) { message = "Tarefa não encontrada."; return false; }

                task.GetType().InvokeMember("Enabled", BindingFlags.SetProperty, null, task,
                                            new object[] { enabled });

                message = enabled
                    ? "Tarefa reativada."
                    : "Tarefa desativada (reversível no Agendador de Tarefas do Windows).";
                return true;
            }
            catch (Exception ex)
            {
                message = "Não foi possível alterar a tarefa: " + ex.Message;
                return false;
            }
            finally
            {
                try { if (task != null && Marshal.IsComObject(task)) Marshal.ReleaseComObject(task); } catch { }
                try { if (service != null && Marshal.IsComObject(service)) Marshal.ReleaseComObject(service); } catch { }
            }
        }

        // ── late-bound COM helpers ──────────────────────────────────────────────────

        private static object? Invoke(object target, string method, params object?[]? args) =>
            target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

        private static object? Get(object target, string property) =>
            target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);

        /// <summary>Collections in the Task Scheduler API are 1-based and indexed via <c>Item</c>.</summary>
        private static object? InvokeIndexer(object collection, int index) =>
            collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection,
                                              new object[] { index });
    }
}
