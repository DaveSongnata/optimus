using System.Collections.Generic;

namespace Optimus.Core.I18n
{
    /// <summary>
    /// English labels for the maintenance operations. Mirrors <see cref="OpsPt"/> key by key. The safety
    /// statements are part of the string, not decoration: the Corel sweep promises the list is shown first,
    /// the browser rule promises cookies and passwords are untouched, and gains stay ranges.
    /// </summary>
    public static partial class LocalizedStrings
    {
        private static Dictionary<string, string> OpsEn() => new Dictionary<string, string>
        {
            ["mnt.op.diag.disk.label"] = "Free space and drive health",
            ["mnt.op.diag.disk.desc"] = "Reads each drive's health status (SMART) and free space. Runs first and changes nothing.",
            ["mnt.op.diag.disk.gain"] = "prevents losing artwork",

            ["mnt.op.corel.savebackups.label"] = "CorelDRAW backup copies",
            ["mnt.op.corel.savebackups.desc"] = "\"Backup_of_*.cdr\" files CorelDRAW creates on every save, right next to your artwork. They usually add up to gigabytes on an old machine. You will see the list with path and size before anything is deleted.",
            ["mnt.op.corel.savebackups.gain"] = "gigabytes — what sets this tool apart",

            ["mnt.op.corel.autorecovery.label"] = "CorelDRAW recovery files",
            ["mnt.op.corel.autorecovery.desc"] = "Old auto-recovery files. CorelDRAW uses them to restore work after a crash; the old ones are of no further use.",
            ["mnt.op.corel.autorecovery.gain"] = "100 MB – 2 GB",

            ["mnt.op.diag.startup.label"] = "Startup audit",
            ["mnt.op.diag.startup.desc"] = "Lists everything that starts with Windows, across all 5 possible sources. You decide what to disable — nothing is disabled on its own, and it is reversible.",
            ["mnt.op.diag.startup.gain"] = "800 MB – 1.5 GB of RAM, permanently",

            ["mnt.op.windows.usertemp.label"] = "Temporary files (per user)",
            ["mnt.op.windows.usertemp.desc"] = "The temp folder of every real user on the machine. Only removes what has been untouched for more than 3 days, so nothing CorelDRAW is still using gets touched.",
            ["mnt.op.windows.usertemp.gain"] = "500 MB – 15 GB",

            ["mnt.op.windows.machinetemp.label"] = "Windows temporary files",
            ["mnt.op.windows.machinetemp.desc"] = "Windows' own temp folder (C:\\Windows\\Temp). Same rule: nothing modified in the last 3 days is touched.",
            ["mnt.op.windows.machinetemp.gain"] = "100 MB – 5 GB",

            ["mnt.op.windows.updatecache.label"] = "Windows Update cache",
            ["mnt.op.windows.updatecache.desc"] = "Installers for updates already applied. Windows re-downloads whatever it needs. Typically 300 MB to 8 GB on an old machine.",
            ["mnt.op.windows.updatecache.gain"] = "300 MB – 8 GB",

            ["mnt.op.windows.crashevidence.label"] = "Crash reports",
            ["mnt.op.windows.crashevidence.desc"] = "Old memory dumps and error reports — a single file can be the size of installed RAM. The 5 newest are kept, because they are the only clue if the machine crashes again.",
            ["mnt.op.windows.crashevidence.gain"] = "100 MB – 64 GB",

            ["mnt.op.windows.deliveryopt.label"] = "Update delivery cache",
            ["mnt.op.windows.deliveryopt.desc"] = "Files Windows keeps in order to share updates with other PCs on the network. They are recreated on demand.",
            ["mnt.op.windows.deliveryopt.gain"] = "100 MB – 4 GB",

            ["mnt.op.windows.recyclebin.label"] = "Recycle Bin",
            ["mnt.op.windows.recyclebin.desc"] = "Shows the size and the 5 largest items BEFORE emptying. Once emptied there is no way to recover them.",
            ["mnt.op.windows.recyclebin.gain"] = "0 – 50 GB",

            ["mnt.op.windows.spool.label"] = "Stuck print queue",
            ["mnt.op.windows.spool.desc"] = "Jammed print jobs that block the whole queue. Requires stopping the print service for a moment.",
            ["mnt.op.windows.spool.gain"] = "unblocks the print queue",

            ["mnt.op.windows.thumbcache.label"] = "Thumbnail cache",
            ["mnt.op.windows.thumbcache.desc"] = "Explorer thumbnails. Reaches several GB for anyone browsing folders of images, and clearing it fixes wrong or blank thumbnails. Explorer restarts for 1–2 seconds.",
            ["mnt.op.windows.thumbcache.gain"] = "100 MB – 5 GB; fixes wrong thumbnails",

            ["mnt.op.browser.cache.label"] = "Browser caches",
            ["mnt.op.browser.cache.desc"] = "Only the cache folders of Chrome, Edge and Firefox. Saved passwords, cookies, history and bookmarks are NOT touched.",
            ["mnt.op.browser.cache.gain"] = "300 MB – 3 GB",

            ["mnt.op.design.scratch.label"] = "Design app scratch files",
            ["mnt.op.design.scratch.desc"] = "Scratch files CorelDRAW, Illustrator and InDesign leave in the temp folder. Only removes what has been unused for more than 3 days.",
            ["mnt.op.design.scratch.gain"] = "50 MB – 2 GB",

            ["mnt.op.windows.cleanmgr.label"] = "Windows Disk Cleanup",
            ["mnt.op.windows.cleanmgr.desc"] = "Uses Microsoft's own tool with a safe configuration: the Downloads folder and the Recycle Bin are EXPLICITLY excluded.",
            ["mnt.op.windows.cleanmgr.gain"] = "500 MB – 10 GB",

            ["mnt.op.windows.componentstore.label"] = "Component store cleanup (DISM)",
            ["mnt.op.windows.componentstore.desc"] = "Removes superseded versions of Windows components. It takes a while, and afterwards old updates can no longer be uninstalled.",
            ["mnt.op.windows.componentstore.gain"] = "1 – 8 GB",

            ["mnt.op.windows.optimize.label"] = "Optimize the drive (media-aware)",
            ["mnt.op.windows.optimize.desc"] = "Defragments a mechanical disk; runs TRIM on an SSD. It never defragments an SSD.",
            ["mnt.op.windows.optimize.gain"] = "performance (mechanical disks only)",

            ["mnt.tab.performance"] = "Performance",
            ["mnt.perf.title"] = "Windows performance adjustments",
            ["mnt.perf.hint"] = "Things Windows trades away for looks or for battery, and which only get "
                              + "in the way on a CorelDRAW workstation. Each adjustment shows how it "
                              + "stands now, what you give up, and can be undone right here.",
            ["mnt.btn.performance"] = "Read the adjustments",
            ["mnt.perf.applyall"] = "Apply the ticked ones",
            ["mnt.perf.col.tweak"] = "Adjustment",
            ["mnt.perf.col.state"] = "Current state",
            ["mnt.perf.col.action"] = "Action",
            ["mnt.perf.state.optimized"] = "already optimized",
            ["mnt.perf.state.default"] = "can be improved",
            ["mnt.perf.state.na"] = "does not apply",
            ["mnt.perf.state.unknown"] = "could not be read",
            ["mnt.perf.current"] = "now: {0}",
            ["mnt.perf.after"] = "becomes: {0}",
            ["mnt.perf.tradeoff"] = "What you give up: {0}",
            ["mnt.perf.explorer"] = "Explorer restarts for 1–2 seconds",
            ["mnt.perf.signout"] = "takes full effect after signing out and back in",
            ["mnt.perf.apply"] = "Apply",
            ["mnt.perf.revert"] = "Undo",
            ["mnt.perf.nofolders"] = "Choose the artwork folders first, on the Cleanup tab.",
            ["mnt.perf.rejected"] = "What this program deliberately does NOT do: disable Windows "
                                  + "services, resize the page file, or force process priority. Those are "
                                  + "the three staples of \"PC booster\" software and they range from "
                                  + "placebo to a support call weeks later.",

            ["mnt.tweak.perf.powerplan.label"] = "High performance power plan",
            ["mnt.tweak.perf.powerplan.desc"] = "On the \"Balanced\" plan Windows lowers the processor's clock and parks cores to save power. On a desktop that stays on all day, that only slows CorelDRAW down when opening and redrawing heavy files.",
            ["mnt.tweak.perf.powerplan.tradeoff"] = "Uses more power. On a laptop, the battery lasts less.",

            ["mnt.tweak.perf.animations.label"] = "Turn off interface animations",
            ["mnt.tweak.perf.animations.desc"] = "Windows animates every window that opens, minimises or appears. Each animation is time you wait for nothing. Turning them off makes the response immediate — it is the adjustment that most changes how fast the machine feels.",
            ["mnt.tweak.perf.animations.tradeoff"] = "The interface becomes plain, with no open/close effects. Font smoothing (ClearType) and thumbnails stay ON: a designer needs both.",

            ["mnt.tweak.perf.transparency.label"] = "Turn off transparency effects",
            ["mnt.tweak.perf.transparency.desc"] = "The frosted glass of the Start menu and the taskbar is recomputed by the graphics card constantly — including while you drag objects in CorelDRAW.",
            ["mnt.tweak.perf.transparency.tradeoff"] = "The Start menu and taskbar become opaque.",

            ["mnt.tweak.perf.menudelay.label"] = "Menus open instantly",
            ["mnt.tweak.perf.menudelay.desc"] = "Windows waits 400 milliseconds before opening a submenu. Multiplied by a working day, that is a lot of waiting for nothing.",
            ["mnt.tweak.perf.menudelay.tradeoff"] = "Nothing.",

            ["mnt.tweak.perf.defender.label"] = "Keep artwork folders out of antivirus scanning",
            ["mnt.tweak.perf.defender.desc"] = "Windows Defender scans every file opened and saved. A 500 MB .cdr is scanned on every save — and a print shop saves all day. Excluding the working folders and CorelDRAW itself removes that wait.",
            ["mnt.tweak.perf.defender.tradeoff"] = "This is a SECURITY trade: files inside those folders stop being checked. Only do it for the shop's own artwork folders, never for the Downloads folder.",

            ["mnt.tweak.perf.searchindex.label"] = "Keep artwork folders out of the search index",
            ["mnt.tweak.perf.searchindex.desc"] = "The Windows indexer reads folders in the background to speed up search. In a folder with thousands of large .cdr files it just keeps reading the disk, without making a search by file name any better.",
            ["mnt.tweak.perf.searchindex.tradeoff"] = "Windows search inside those folders becomes slower (but keeps working).",

            ["mnt.op.diag.memory.label"] = "Memory and crash diagnosis",
            ["mnt.op.diag.memory.desc"] = "How much memory is genuinely committed, which programs are consuming it, and how many times this machine crashed in the last 30 days.",
            ["mnt.op.diag.memory.gain"] = "real information",
        };
    }
}
