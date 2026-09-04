using System.Collections.Generic;

namespace Optimus.Core.I18n
{
    /// <summary>
    /// Spanish labels for the maintenance operations. Mirrors <see cref="OpsPt"/> key by key, keeping the
    /// same promises: the Corel backup sweep always states that the list is shown before deleting, the
    /// browser rule always states that cookies/passwords are untouched, and every gain stays a range.
    /// </summary>
    public static partial class LocalizedStrings
    {
        private static Dictionary<string, string> OpsEs() => new Dictionary<string, string>
        {
            ["mnt.op.diag.disk.label"] = "Espacio libre y salud de los discos",
            ["mnt.op.diag.disk.desc"] = "Lee el estado de salud (SMART) y el espacio libre de cada disco. Se ejecuta primero y no modifica nada.",
            ["mnt.op.diag.disk.gain"] = "evita perder arte",

            ["mnt.op.corel.savebackups.label"] = "Copias de seguridad de CorelDRAW",
            ["mnt.op.corel.savebackups.desc"] = "Archivos \"Backup_of_*.cdr\" que CorelDRAW crea en cada guardado, junto a tu arte. Suelen sumar gigabytes en una máquina antigua. Verás la lista con ruta y tamaño antes de borrar.",
            ["mnt.op.corel.savebackups.gain"] = "gigabytes — el diferencial de este programa",

            ["mnt.op.corel.autorecovery.label"] = "Archivos de recuperación de CorelDRAW",
            ["mnt.op.corel.autorecovery.desc"] = "Archivos de recuperación automática antiguos. CorelDRAW los usa para restaurar el trabajo tras un bloqueo; los antiguos ya no sirven.",
            ["mnt.op.corel.autorecovery.gain"] = "100 MB – 2 GB",

            ["mnt.op.diag.startup.label"] = "Auditoría de inicio",
            ["mnt.op.diag.startup.desc"] = "Lista todo lo que se abre junto con Windows, en las 5 orígenes posibles. Tú decides qué desactivar — nada se desactiva solo, y es reversible.",
            ["mnt.op.diag.startup.gain"] = "800 MB – 1,5 GB de RAM, permanente",

            ["mnt.op.windows.usertemp.label"] = "Archivos temporales (por usuario)",
            ["mnt.op.windows.usertemp.desc"] = "La carpeta de temporales de cada usuario real de la máquina. Solo quita lo que lleva más de 3 días sin uso, para no tocar nada que CorelDRAW siga usando.",
            ["mnt.op.windows.usertemp.gain"] = "500 MB – 15 GB",

            ["mnt.op.windows.machinetemp.label"] = "Archivos temporales de Windows",
            ["mnt.op.windows.machinetemp.desc"] = "La carpeta de temporales de Windows (C:\\Windows\\Temp). Mismo criterio: nada modificado en los últimos 3 días se toca.",
            ["mnt.op.windows.machinetemp.gain"] = "100 MB – 5 GB",

            ["mnt.op.windows.updatecache.label"] = "Caché de Windows Update",
            ["mnt.op.windows.updatecache.desc"] = "Instaladores de actualizaciones ya aplicadas. Windows vuelve a descargar lo que necesite. Suele ocupar de 300 MB a 8 GB en una máquina antigua.",
            ["mnt.op.windows.updatecache.gain"] = "300 MB – 8 GB",

            ["mnt.op.windows.crashevidence.label"] = "Informes de bloqueo",
            ["mnt.op.windows.crashevidence.desc"] = "Volcados de memoria e informes de error antiguos — un solo archivo puede pesar como la memoria RAM. Los 5 más recientes se conservan, porque son la única pista si la máquina vuelve a bloquearse.",
            ["mnt.op.windows.crashevidence.gain"] = "100 MB – 64 GB",

            ["mnt.op.windows.deliveryopt.label"] = "Caché de distribución de actualizaciones",
            ["mnt.op.windows.deliveryopt.desc"] = "Archivos que Windows guarda para compartir actualizaciones con otros PC de la red. Se recrean cuando hacen falta.",
            ["mnt.op.windows.deliveryopt.gain"] = "100 MB – 4 GB",

            ["mnt.op.windows.recyclebin.label"] = "Papelera de reciclaje",
            ["mnt.op.windows.recyclebin.desc"] = "Muestra el tamaño y los 5 elementos más grandes ANTES de vaciarla. Después de vaciarla no hay forma de recuperar.",
            ["mnt.op.windows.recyclebin.gain"] = "0 – 50 GB",

            ["mnt.op.windows.spool.label"] = "Cola de impresión atascada",
            ["mnt.op.windows.spool.desc"] = "Trabajos de impresión atascados que bloquean toda la cola. Requiere detener un instante el servicio de impresión.",
            ["mnt.op.windows.spool.gain"] = "desatasca la cola de impresión",

            ["mnt.op.windows.thumbcache.label"] = "Caché de miniaturas",
            ["mnt.op.windows.thumbcache.desc"] = "Miniaturas del Explorador. Llega a varios GB en quien navega por carpetas de imágenes, y limpiarla arregla las miniaturas erróneas o en blanco. El Explorador se reinicia por 1–2 segundos.",
            ["mnt.op.windows.thumbcache.gain"] = "100 MB – 5 GB; arregla miniaturas erróneas",

            ["mnt.op.browser.cache.label"] = "Caché de los navegadores",
            ["mnt.op.browser.cache.desc"] = "Solo las carpetas de caché de Chrome, Edge y Firefox. Las contraseñas guardadas, cookies, historial y favoritos NO se tocan.",
            ["mnt.op.browser.cache.gain"] = "300 MB – 3 GB",

            ["mnt.op.design.scratch.label"] = "Archivos temporales de programas de diseño",
            ["mnt.op.design.scratch.desc"] = "Borradores que CorelDRAW, Illustrator e InDesign dejan en la carpeta de temporales. Solo quita lo que lleva más de 3 días sin uso.",
            ["mnt.op.design.scratch.gain"] = "50 MB – 2 GB",

            ["mnt.op.windows.cleanmgr.label"] = "Liberador de espacio de Windows",
            ["mnt.op.windows.cleanmgr.desc"] = "Usa la propia herramienta de Microsoft, con una configuración segura: la carpeta Descargas y la Papelera quedan EXPLÍCITAMENTE fuera.",
            ["mnt.op.windows.cleanmgr.gain"] = "500 MB – 10 GB",

            ["mnt.op.windows.componentstore.label"] = "Limpieza del almacén de componentes (DISM)",
            ["mnt.op.windows.componentstore.desc"] = "Quita versiones antiguas de componentes de Windows. Tarda, y después las actualizaciones antiguas ya no se pueden desinstalar.",
            ["mnt.op.windows.componentstore.gain"] = "1 – 8 GB",

            ["mnt.op.windows.optimize.label"] = "Optimizar el disco (según el medio)",
            ["mnt.op.windows.optimize.desc"] = "En disco mecánico desfragmenta; en SSD ejecuta el TRIM. Nunca desfragmenta un SSD.",
            ["mnt.op.windows.optimize.gain"] = "rendimiento (solo en disco mecánico)",

            ["mnt.tab.performance"] = "Rendimiento",
            ["mnt.perf.title"] = "Ajustes de rendimiento de Windows",
            ["mnt.perf.hint"] = "Cosas que Windows cambia por adorno o por ahorro de energía y que en una "
                              + "estación con CorelDRAW solo estorban. Cada ajuste muestra cómo está "
                              + "ahora, qué cedes a cambio, y se puede deshacer aquí mismo.",
            ["mnt.btn.performance"] = "Leer los ajustes",
            ["mnt.perf.applyall"] = "Aplicar los marcados",
            ["mnt.perf.col.tweak"] = "Ajuste",
            ["mnt.perf.col.state"] = "Cómo está",
            ["mnt.perf.col.action"] = "Acción",
            ["mnt.perf.state.optimized"] = "ya optimizado",
            ["mnt.perf.state.default"] = "puede mejorar",
            ["mnt.perf.state.na"] = "no se aplica",
            ["mnt.perf.state.unknown"] = "no se pudo leer",
            ["mnt.perf.current"] = "ahora: {0}",
            ["mnt.perf.after"] = "queda: {0}",
            ["mnt.perf.tradeoff"] = "Lo que cedes: {0}",
            ["mnt.perf.explorer"] = "el Explorador se reinicia por 1–2 segundos",
            ["mnt.perf.signout"] = "tiene efecto completo después de cerrar y volver a iniciar sesión",
            ["mnt.perf.apply"] = "Aplicar",
            ["mnt.perf.revert"] = "Deshacer",
            ["mnt.perf.nofolders"] = "Elige antes las carpetas de arte, en la pestaña Limpieza.",
            ["mnt.perf.rejected"] = "Lo que este programa NO hace, a propósito: desactivar servicios de "
                                  + "Windows, tocar el archivo de paginación y forzar la prioridad de "
                                  + "procesos. Son los tres clásicos del \"acelerador de PC\" y van del "
                                  + "placebo a una llamada de soporte semanas después.",

            ["mnt.tweak.perf.powerplan.label"] = "Plan de energía de alto rendimiento",
            ["mnt.tweak.perf.powerplan.desc"] = "En el plan \"Equilibrado\" Windows baja la frecuencia del procesador y \"aparca\" núcleos para ahorrar energía. En una computadora de escritorio encendida todo el día, eso solo retrasa a CorelDRAW al abrir y al redibujar archivos pesados.",
            ["mnt.tweak.perf.powerplan.tradeoff"] = "Consume más energía. En un portátil, la batería dura menos.",

            ["mnt.tweak.perf.animations.label"] = "Apagar las animaciones de la interfaz",
            ["mnt.tweak.perf.animations.desc"] = "Windows anima cada ventana que se abre, se minimiza o aparece. Cada animación es tiempo que esperas sin necesidad. Apagarlas deja la respuesta inmediata — es el ajuste que más cambia la sensación de máquina rápida.",
            ["mnt.tweak.perf.animations.tradeoff"] = "La interfaz queda \"seca\", sin los efectos de abrir y cerrar. El suavizado de texto (ClearType) y las miniaturas siguen activos: un diseñador necesita los dos.",

            ["mnt.tweak.perf.transparency.label"] = "Apagar los efectos de transparencia",
            ["mnt.tweak.perf.transparency.desc"] = "El vidrio esmerilado del menú Inicio y de la barra de tareas lo recalcula la placa de video todo el tiempo, incluso mientras arrastras objetos en CorelDRAW.",
            ["mnt.tweak.perf.transparency.tradeoff"] = "El menú Inicio y la barra de tareas quedan opacos.",

            ["mnt.tweak.perf.menudelay.label"] = "Los menús se abren al instante",
            ["mnt.tweak.perf.menudelay.desc"] = "Windows espera 400 milisegundos antes de abrir un submenú. Multiplicado por un día de trabajo, es bastante espera al pedo.",
            ["mnt.tweak.perf.menudelay.tradeoff"] = "Nada.",

            ["mnt.tweak.perf.defender.label"] = "Sacar las carpetas de arte del análisis del antivirus",
            ["mnt.tweak.perf.defender.desc"] = "Windows Defender analiza cada archivo que se abre y se guarda. Un .cdr de 500 MB se analiza en cada guardado — y la imprenta guarda todo el día. Excluir las carpetas de trabajo y el propio CorelDRAW elimina esa espera.",
            ["mnt.tweak.perf.defender.tradeoff"] = "Es un intercambio de SEGURIDAD: los archivos dentro de esas carpetas dejan de verificarse. Hazlo solo en carpetas de arte de la propia imprenta, nunca en la carpeta de descargas.",

            ["mnt.tweak.perf.searchindex.label"] = "Sacar las carpetas de arte del índice de búsqueda",
            ["mnt.tweak.perf.searchindex.desc"] = "El indexador de Windows lee las carpetas en segundo plano para acelerar la búsqueda. En una carpeta con miles de .cdr grandes, se queda leyendo disco sin que la búsqueda por nombre de archivo mejore en nada.",
            ["mnt.tweak.perf.searchindex.tradeoff"] = "La búsqueda de Windows dentro de esas carpetas se vuelve más lenta (pero sigue funcionando).",

            ["mnt.op.diag.memory.label"] = "Diagnóstico de memoria y bloqueos",
            ["mnt.op.diag.memory.desc"] = "Cuánta memoria está realmente comprometida, qué programas la consumen y cuántas veces se bloqueó esta máquina en los últimos 30 días.",
            ["mnt.op.diag.memory.gain"] = "información real",
        };
    }
}
