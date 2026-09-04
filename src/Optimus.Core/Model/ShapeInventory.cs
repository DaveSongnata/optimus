using System.Collections.Generic;

namespace Optimus.Core.Model
{
    /// <summary>One shape as seen by the traversal. Plain data — no COM.</summary>
    public sealed class ShapeRecord
    {
        public ShapeRecord(CorelShapeKind kind, int nodes = 0, bool inPowerClip = false)
        {
            Kind = kind;
            Nodes = nodes;
            InPowerClip = inPowerClip;
        }

        public CorelShapeKind Kind { get; }

        /// <summary>Node count for curve shapes; 0 for everything else.</summary>
        public int Nodes { get; }

        /// <summary>True when the shape was found inside a PowerClip's contents.</summary>
        public bool InPowerClip { get; }
    }

    /// <summary>
    /// Aggregated view of everything the traversal saw. This is the evidence that the walk
    /// actually reached the document: Optimus v1.0 reported success while never descending into
    /// PowerClips, so a design file looked almost empty to it.
    /// </summary>
    public sealed class ShapeInventory
    {
        private ShapeInventory(
            int totalShapes,
            long totalNodes,
            int shapesInPowerClip,
            int reducibleShapes,
            int liveEffects,
            int rasterShapes,
            IReadOnlyDictionary<CorelShapeKind, int> countByKind)
        {
            TotalShapes = totalShapes;
            TotalNodes = totalNodes;
            ShapesInPowerClip = shapesInPowerClip;
            ReducibleShapes = reducibleShapes;
            LiveEffects = liveEffects;
            RasterShapes = rasterShapes;
            CountByKind = countByKind;
        }

        public int TotalShapes { get; }
        public long TotalNodes { get; }
        public int ShapesInPowerClip { get; }

        /// <summary>Shapes carrying Bézier geometry that node reduction can act on.</summary>
        public int ReducibleShapes { get; }

        /// <summary>Live effects — the fluidity cost driver (Phase 4).</summary>
        public int LiveEffects { get; }

        /// <summary>Shapes carrying raster payload (bitmap/OLE/EPS).</summary>
        public int RasterShapes { get; }

        public IReadOnlyDictionary<CorelShapeKind, int> CountByKind { get; }

        public static ShapeInventory From(IEnumerable<ShapeRecord> records)
        {
            var byKind = new Dictionary<CorelShapeKind, int>();
            int total = 0, inPowerClip = 0, reducible = 0, effects = 0, raster = 0;
            long nodes = 0;

            foreach (ShapeRecord r in records)
            {
                total++;
                nodes += r.Nodes;
                if (r.InPowerClip) inPowerClip++;
                if (GeometryClassifier.HasReducibleGeometry(r.Kind)) reducible++;
                if (GeometryClassifier.IsLiveEffect(r.Kind)) effects++;
                if (GeometryClassifier.CarriesRaster(r.Kind)) raster++;

                byKind.TryGetValue(r.Kind, out int n);
                byKind[r.Kind] = n + 1;
            }

            return new ShapeInventory(total, nodes, inPowerClip, reducible, effects, raster, byKind);
        }
    }
}
