using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Optimus.Interop
{
    /// <summary>A set of shapes that carry the same art, placed differently.</summary>
    public sealed class RepetitionGroup
    {
        /// <summary>The shapes, in the order found. The first is the natural symbol definition.</summary>
        public List<object> Shapes { get; } = new List<object>();

        /// <summary>Node count of one member, for the report.</summary>
        public int Nodes { get; set; }

        /// <summary>True when membership was proven by comparing full coordinates, not just a cheap key.</summary>
        public bool Exact { get; set; }

        public int Copies => Math.Max(0, Shapes.Count - 1);
    }

    public sealed class RepetitionReport
    {
        public List<RepetitionGroup> Groups { get; } = new List<RepetitionGroup>();

        public int ShapesExamined { get; set; }
        public int DistinctShapes { get; set; }

        /// <summary>Shapes that are a copy of another one.</summary>
        public int RepeatedShapes { get; set; }

        /// <summary>
        /// False when the exact geometry comparison could not run on this CorelDRAW build. Grouping is
        /// then <b>reporting-only</b> and must never drive a mutation.
        /// </summary>
        public bool ExactComparisonAvailable { get; set; }

        public string Note { get; set; } = "";

        /// <summary>True when EVERY candidate was compared by coordinates, not sampled.</summary>
        public bool ConfirmedExactly { get; set; }

        /// <summary>How many shapes the exact comparison actually touched.</summary>
        public int ShapesConfirmedExactly { get; set; }

        /// <summary>Groups formed from the cheap key alone, beyond the sample budget.</summary>
        public int EstimatedGroups { get; set; }

        /// <summary>
        /// Whether this report may drive a change to the document. A sampled run may NOT: turning shapes
        /// that merely measure alike into one symbol would replace art with different art.
        /// </summary>
        public bool SafeToMutate => ExactComparisonAvailable && ConfirmedExactly && EstimatedGroups == 0;

        public double RepeatedSharePercent =>
            ShapesExamined <= 0 ? 0 : Math.Round(RepeatedShapes * 100.0 / ShapesExamined, 1);
    }

    /// <summary>
    /// Finds shapes whose geometry is identical, so the drawing can store each one once.
    ///
    /// <para>
    /// This is the biggest lever the product has (O15): on a real file, 16.914 objects hold only 4.789
    /// distinct coordinate blocks, and storing each once is worth <b>58,2% of the file, losslessly</b>.
    /// The file-level census already proves the opportunity exists; this class is what finds the same
    /// shapes through COM so they can actually be instanced.
    /// </para>
    /// <para>
    /// <b>Two stages, and the second is not optional.</b> A cheap key (bounding box + area + subpath
    /// count) forms candidate groups in one COM call each; only inside a candidate group is the full
    /// coordinate array fetched and compared. Grouping shapes that merely LOOK alike and turning them
    /// into one symbol would silently replace one drawing with another — so when the exact comparison is
    /// unavailable, this reports and refuses rather than guessing.
    /// </para>
    /// </summary>
    public sealed class RepetitionFinder
    {
        /// <summary>
        /// How many shapes the exact comparison is allowed to touch during a MEASUREMENT run.
        ///
        /// <para>
        /// Reading a shape's coordinate array means walking every <c>CurveElement</c> through the
        /// late-bound binder — four member reads per node. On a 14.342-curve drawing that is over a
        /// million late-bound accesses and the analysis appears to hang (measured on Davi's machine:
        /// still running after five minutes). The cheap key already describes the repetition structure;
        /// the exact pass only has to prove that CorelDRAW hands the coordinates over at all, and that
        /// the cheap key is not merging shapes that differ. A sample answers both.
        /// </para>
        /// <para>
        /// A MUTATION must still confirm every shape it touches — see <see cref="Find"/>'s
        /// <c>confirmAll</c>.
        /// </para>
        /// </summary>
        private const int SampleLimit = 120;

        private readonly Action<string>? _log;

        /// <summary>
        /// True once the coordinate read has failed. Whether CorelDRAW hands over <c>CurveElement</c> is a
        /// property of the BUILD, not of the shape — so once it fails it fails for all of them, and
        /// retrying is pure cost.
        /// </summary>
        private bool _geometryFailureLogged;

        private bool _geometryUnavailable;

        public RepetitionFinder(Action<string>? log = null) => _log = log;

        /// <summary>
        /// Groups a collection of shapes by identical geometry.
        ///
        /// <para>
        /// <paramref name="shapes"/> are NOT released here — the caller owns them, because a group's
        /// members are handed back for conversion.
        /// </para>
        /// </summary>
        /// <summary>
        /// <paramref name="confirmAll"/> false = MEASUREMENT: the coordinate comparison runs on a sample
        /// only, and groups are formed from the cheap key. True = the caller is about to MUTATE, so every
        /// candidate is confirmed exactly and the run costs what it costs.
        /// </summary>
        public RepetitionReport Find(IList<object> shapes, bool confirmAll = false)
        {
            var report = new RepetitionReport();
            if (shapes == null || shapes.Count == 0) return report;

            report.ShapesExamined = shapes.Count;
            report.ConfirmedExactly = confirmAll;
            int exactBudget = confirmAll ? int.MaxValue : SampleLimit;

            // Stage 1: cheap bucketing. Two shapes with different bounds or area cannot be identical,
            // and this costs one COM round-trip per shape instead of one per node.
            var buckets = new Dictionary<string, List<object>>(StringComparer.Ordinal);
            foreach (object shape in shapes)
            {
                string key = CheapKey(shape);
                if (key == null!) continue;

                if (!buckets.TryGetValue(key, out List<object> bucket))
                {
                    bucket = new List<object>();
                    buckets[key] = bucket;
                }
                bucket.Add(shape);
            }

            _log?.Invoke($"repetição: {shapes.Count} formas em {buckets.Count} grupos candidatos");

            // Stage 2: confirm by coordinates. Only bucket members can possibly match, so the expensive
            // call runs on a fraction of the document.
            bool exactWorked = false;
            bool exactTried = false;
            int exactSpent = 0;

            foreach (KeyValuePair<string, List<object>> bucket in buckets)
            {
                if (bucket.Value.Count == 1)
                {
                    report.DistinctShapes++;
                    continue;
                }

                // Within the sample budget, split the bucket by real coordinates. Beyond it, the bucket
                // is reported AS a group — which is why a run that did not confirm everything is marked
                // and may not drive a mutation.
                if (exactSpent < exactBudget)
                {
                    exactTried = true;
                    exactSpent += bucket.Value.Count;

                    foreach (RepetitionGroup group in ConfirmByCoordinates(bucket.Value, ref exactWorked))
                    {
                        report.DistinctShapes++;
                        report.RepeatedShapes += group.Copies;
                        if (group.Copies > 0) report.Groups.Add(group);
                    }
                    continue;
                }

                var estimated = new RepetitionGroup { Exact = false };
                estimated.Shapes.AddRange(bucket.Value);
                report.DistinctShapes++;
                report.RepeatedShapes += estimated.Copies;
                report.Groups.Add(estimated);
                report.EstimatedGroups++;
            }

            report.ExactComparisonAvailable = !exactTried || exactWorked;
            report.ShapesConfirmedExactly = Math.Min(exactSpent, shapes.Count);

            if (!report.ExactComparisonAvailable)
                report.Note = "O CorelDRAW desta máquina não devolveu as coordenadas para comparação "
                            + "exata; a repetição foi apenas ESTIMADA e não será usada para alterar o "
                            + "arquivo.";
            else if (report.EstimatedGroups > 0)
                report.Note = $"Comparação exata feita em {report.ShapesConfirmedExactly} formas "
                            + $"(amostra); o restante foi agrupado pela medida aproximada. "
                            + "Antes de alterar o arquivo, todos serão conferidos.";

            return report;
        }

        /// <summary>
        /// Cheap identity key: bounding box size and area, at full double precision.
        ///
        /// <para>
        /// Deliberately excludes POSITION — repeated art is the same shape in different places, so
        /// including x/y would put every copy in its own bucket and find nothing. Size and area do not
        /// change under translation.
        /// </para>
        /// </summary>
        private string CheapKey(object shape)
        {
            try
            {
                dynamic s = shape;

                double x = 0, y = 0, w = 0, h = 0;
                s.GetBoundingBox(out x, out y, out w, out h);

                // Bounding box ONLY. The first version also mixed in Curve.Area, which looked cheap and
                // was not: reading .Curve materialises an RCW per shape (leaked, blocking the STA thread
                // later — O9) and .Area integrates the whole path, so it costs O(nodes) exactly like
                // counting them. Measured effect: the analysis ran for minutes and looked frozen.
                // Width and height alone are a perfectly good pre-filter — the exact coordinate
                // comparison is what decides membership anyway.
                return w.ToString("R", CultureInfo.InvariantCulture) + "|"
                     + h.ToString("R", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null!;
            }
        }

        /// <summary>
        /// Splits a candidate bucket into groups of shapes with byte-identical coordinates.
        ///
        /// <para>
        /// Sets <paramref name="exactWorked"/> when at least one coordinate array was actually read. If
        /// it stays false the whole run is downgraded to reporting-only — see the remark on the class.
        /// </para>
        /// </summary>
        private List<RepetitionGroup> ConfirmByCoordinates(List<object> candidates, ref bool exactWorked)
        {
            var groups = new List<RepetitionGroup>();
            var byHash = new Dictionary<string, RepetitionGroup>(StringComparer.Ordinal);

            foreach (object shape in candidates)
            {
                string? hash = GeometryHash(shape, out int nodes);

                if (hash == null)
                {
                    // No coordinates: this shape gets its own group, never merged on a guess.
                    var alone = new RepetitionGroup { Exact = false, Nodes = nodes };
                    alone.Shapes.Add(shape);
                    groups.Add(alone);
                    continue;
                }

                exactWorked = true;

                if (!byHash.TryGetValue(hash, out RepetitionGroup group))
                {
                    group = new RepetitionGroup { Exact = true, Nodes = nodes };
                    byHash[hash] = group;
                    groups.Add(group);
                }
                group.Shapes.Add(shape);
            }

            return groups;
        }

        /// <summary>
        /// Hash of a shape's full coordinate array, or null when CorelDRAW would not hand it over.
        ///
        /// <para>
        /// <c>Curve.GetCurveInfo()</c> (typelib 3764) returns a SAFEARRAY of the <c>CurveElement</c>
        /// struct — <c>PositionX</c>, <c>PositionY</c>, <c>ElementType</c>, <c>NodeType</c>, <c>Flags</c>
        /// (typelib 1751). Marshalling a record array through late binding is not guaranteed on every
        /// build, so a failure here returns null and the caller degrades to reporting instead of
        /// pretending. It is ONE call per shape, not per node — the O9 rule.
        /// </para>
        /// </summary>
        private string? GeometryHash(object shape, out int nodes)
        {
            nodes = 0;

            // Once the build has refused, stop asking: 14.342 doomed calls cost 29 seconds and told us
            // nothing the first one had not.
            if (_geometryUnavailable) return null;

            object? curve = null;

            try
            {
                curve = (object)((dynamic)shape).Curve;
                if (curve == null) return null;

                object? info = curve.GetType().InvokeMember(
                    "GetCurveInfo", BindingFlags.InvokeMethod, null, curve, null);

                if (!(info is Array array) || array.Length == 0) return null;

                nodes = array.Length;

                // Member accessors are resolved ONCE for the whole run. Reading them through `dynamic`
                // per element was the defect that made the analysis look frozen: four late-bound reads
                // per node, over a million on a real drawing.
                object? first = array.GetValue(0);
                if (first == null) return null;
                if (!BindAccessors(first.GetType())) return null;

                const ulong offsetBasis = 14695981039346656037;
                const ulong prime = 1099511628211;
                ulong hash = offsetBasis;

                foreach (object? element in array)
                {
                    if (element == null) continue;

                    // Only the geometry matters: coordinates plus how the point behaves. Position is
                    // in object space, so two copies of the same art hash the same however they are
                    // placed on the page.
                    hash = Mix(hash, ReadDouble(_positionX, element), prime);
                    hash = Mix(hash, ReadDouble(_positionY, element), prime);
                    hash = MixInt(hash, ReadInt(_elementType, element), prime);
                    hash = MixInt(hash, ReadInt(_nodeType, element), prime);
                }

                return nodes.ToString(CultureInfo.InvariantCulture) + ":" + hash.ToString("x16");
            }
            catch (Exception ex)
            {
                // Logged ONCE per run. The first version logged per shape and wrote 11.190 identical
                // lines — a 1,28 MB log for a single fact, which buries every other line that mattered.
                if (!_geometryFailureLogged)
                {
                    _geometryFailureLogged = true;
                    _geometryUnavailable = true;
                    _log?.Invoke("GetCurveInfo indisponível neste CorelDRAW (não será tentado de novo): "
                               + (ex.InnerException?.Message ?? ex.Message));
                }
                return null;
            }
            finally
            {
                try
                {
                    if (curve != null && System.Runtime.InteropServices.Marshal.IsComObject(curve))
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(curve);
                }
                catch (Exception) { }
            }
        }

        // ── cached accessors for CurveElement (typelib STRUCT, dump 1751) ───────────

        private MemberInfo? _positionX, _positionY, _elementType, _nodeType;
        private Type? _boundType;

        /// <summary>
        /// Resolves the five <c>CurveElement</c> members once. Handles both shapes the marshaller can
        /// produce — a struct with public fields, or a class with properties — and gives up cleanly if
        /// it is neither, which is what keeps the caller honest about not having compared anything.
        /// </summary>
        private bool BindAccessors(Type type)
        {
            if (_boundType == type) return _positionX != null && _positionY != null;

            _boundType = type;
            _positionX = Member(type, "PositionX");
            _positionY = Member(type, "PositionY");
            _elementType = Member(type, "ElementType");
            _nodeType = Member(type, "NodeType");

            if (_positionX == null || _positionY == null)
            {
                _log?.Invoke("CurveElement sem PositionX/PositionY acessíveis (tipo " + type.FullName + ")");
                return false;
            }
            return true;
        }

        private static MemberInfo? Member(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            return (MemberInfo?)type.GetField(name, flags) ?? type.GetProperty(name, flags);
        }

        private static object? Value(MemberInfo? member, object instance)
        {
            try
            {
                if (member is FieldInfo f) return f.GetValue(instance);
                if (member is PropertyInfo p) return p.GetValue(instance, null);
            }
            catch (Exception) { }
            return null;
        }

        private static double ReadDouble(MemberInfo? member, object instance)
        {
            object? v = Value(member, instance);
            return v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        private static int ReadInt(MemberInfo? member, object instance)
        {
            object? v = Value(member, instance);
            try { return v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return 0; }
        }

        private static ulong Mix(ulong hash, double value, ulong prime)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            for (int i = 0; i < 8; i++)
            {
                hash ^= (byte)(bits >> (i * 8));
                hash *= prime;
            }
            return hash;
        }

        private static ulong MixInt(ulong hash, int value, ulong prime)
        {
            for (int i = 0; i < 4; i++)
            {
                hash ^= (byte)(value >> (i * 8));
                hash *= prime;
            }
            return hash;
        }
    }
}
