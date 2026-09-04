using System;
using System.IO;
using System.Reflection;
using Whisper.net;
using Whisper.net.LibraryLoader;

class Program
{
    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "plugin");
            string name = new AssemblyName(args.Name).Name + ".dll";
            string path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                Console.WriteLine("AssemblyResolve: " + name + " <- " + dir);
                return Assembly.LoadFrom(path);
            }
            Console.WriteLine("AssemblyResolve MISS: " + name);
            return null;
        };
    }

    static void Main()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string pluginDir = Path.GetFullPath(Path.Combine(baseDir, "..", "plugin"));
        Console.WriteLine("Host BaseDir: " + baseDir);
        Console.WriteLine("Plugin dir:   " + pluginDir);
        Console.WriteLine("Runtimes exists under plugin: " + Directory.Exists(Path.Combine(pluginDir, "runtimes", "win-x64")));
        string modelPath = Path.Combine(pluginDir, "ggml-model.bin");
        Console.WriteLine("Model exists: " + File.Exists(modelPath) + " at " + modelPath);

        try
        {
            using var factory = WhisperFactory.FromPath(modelPath);
            Console.WriteLine("SUCCESS: factory created");
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED: " + ex.Message);
        }
    }
}
