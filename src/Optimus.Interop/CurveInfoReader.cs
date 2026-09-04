using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Optimus.Interop
{
    /// <summary>One point of a curve, as CorelDRAW stores it.</summary>
    public struct CurvePoint
    {
        public double X;
        public double Y;

        /// <summary><c>cdrCurveElementType</c>: Start=0, Line=1, Curve=2, Control=3.</summary>
        public int ElementType;

        /// <summary><c>cdrNodeType</c>: Cusp=0, Smooth=1, Symmetrical=2.</summary>
        public int NodeType;
    }

    /// <summary>
    /// Reads a curve's points by calling <c>GetCurveInfo</c> through raw IDispatch and decoding the
    /// returned SAFEARRAY by hand.
    ///
    /// <para>
    /// WHY. <c>Curve.GetCurveInfo()</c> returns <c>CurveElement[]</c>, an array of a typelib STRUCT. Any
    /// managed call path — <c>dynamic</c>, <c>InvokeMember</c> — makes the CLR try to map that record to
    /// a managed value class, and without a registered interop assembly it fails outright:
    /// <i>"The specified record cannot be mapped to a managed value class"</i> (measured, CorelDRAW 2024).
    /// Shipping an interop assembly is not an option: one binary has to serve 2024/25/26, whose typelib
    /// GUIDs differ.
    /// </para>
    /// <para>
    /// So the CLR is kept out of it. <c>IDispatch::Invoke</c> is called directly, the VARIANT is received
    /// into a buffer this class owns, and the SAFEARRAY of records is walked as bytes using the layout
    /// the typelib documents (dump 1751): <c>Double PositionX, Double PositionY,
    /// cdrCurveElementType ElementType, cdrNodeType NodeType, Byte Flags</c>.
    /// </para>
    /// <para>
    /// Every assumption is CHECKED at runtime rather than trusted — the variant type, the dimension
    /// count, and above all the element size, which must match the layout above. A mismatch returns null
    /// instead of decoding garbage into coordinates that would later be used to replace the customer's
    /// art.
    /// </para>
    /// </summary>
    public sealed class CurveInfoReader
    {
        // ── IDispatch ───────────────────────────────────────────────────────────────

        [ComImport, Guid("00020400-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDispatch
        {
            void GetTypeInfoCount(out uint count);
            void GetTypeInfo(uint index, uint lcid, out IntPtr typeInfo);

            void GetIDsOfNames(
                ref Guid riid,
                [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names,
                uint count, uint lcid,
                [MarshalAs(UnmanagedType.LPArray)] int[] dispIds);

            [PreserveSig]
            int Invoke(
                int dispIdMember, ref Guid riid, uint lcid, ushort flags,
                ref DISPPARAMS parameters, IntPtr result, IntPtr exceptionInfo, IntPtr argError);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPPARAMS
        {
            public IntPtr rgvarg;
            public IntPtr rgdispidNamedArgs;
            public uint cArgs;
            public uint cNamedArgs;
        }

        private const ushort DispatchMethod = 1;
        private const ushort DispatchPropertyGet = 2;

        private static Guid _iidNull = Guid.Empty;

        // ── SAFEARRAY ───────────────────────────────────────────────────────────────

        [DllImport("oleaut32.dll")] private static extern uint SafeArrayGetElemsize(IntPtr psa);
        [DllImport("oleaut32.dll")] private static extern uint SafeArrayGetDim(IntPtr psa);
        [DllImport("oleaut32.dll")] private static extern int SafeArrayGetLBound(IntPtr psa, uint dim, out int bound);
        [DllImport("oleaut32.dll")] private static extern int SafeArrayGetUBound(IntPtr psa, uint dim, out int bound);
        [DllImport("oleaut32.dll")] private static extern int SafeArrayAccessData(IntPtr psa, out IntPtr data);
        [DllImport("oleaut32.dll")] private static extern int SafeArrayUnaccessData(IntPtr psa);
        [DllImport("oleaut32.dll")] private static extern int VariantClear(IntPtr variant);

        /// <summary>VT_ARRAY (0x2000) combined with VT_RECORD (36).</summary>
        private const ushort VtArrayRecord = 0x2000 | 36;

        /// <summary>
        /// Layout of <c>CurveElement</c>: two doubles, two 4-byte enums, one byte — padded to the
        /// natural 8-byte alignment of a double.
        /// </summary>
        private const int ExpectedElementSize = 32;

        /// <summary>Alternative packing, without trailing padding.</summary>
        private const int AlternateElementSize = 25;

        private readonly Action<string>? _log;
        private bool _unsupported;
        private bool _logged;

        public CurveInfoReader(Action<string>? log = null) => _log = log;

        /// <summary>True once the build has proven it will not hand over curve data.</summary>
        public bool Unsupported => _unsupported;

        /// <summary>
        /// Reads the points of a shape's curve, or null when they cannot be obtained. Never throws.
        /// </summary>
        public List<CurvePoint>? Read(object shape)
        {
            if (_unsupported) return null;

            object? curve = null;
            try
            {
                // DisplayCurve (typelib 5997) gives the curve of ANY shape — rectangle, ellipse, polygon
                // — WITHOUT converting it, so non-curves can be fingerprinted without touching the
                // document. On this file that matters: 2.523 of its shapes are ellipses.
                curve = GetProperty(shape, "DisplayCurve") ?? GetProperty(shape, "Curve");
                if (curve == null) return null;

                return ReadFromCurve(curve);
            }
            catch (Exception ex)
            {
                Fail("leitura da curva falhou: " + ex.Message);
                return null;
            }
            finally
            {
                if (curve != null && Marshal.IsComObject(curve)) Marshal.ReleaseComObject(curve);
            }
        }

        private List<CurvePoint>? ReadFromCurve(object curve)
        {
            if (!(curve is IDispatch dispatch))
            {
                Fail("o objeto Curve não expõe IDispatch");
                return null;
            }

            int dispId;
            try
            {
                var ids = new int[1];
                dispatch.GetIDsOfNames(ref _iidNull, new[] { "GetCurveInfo" }, 1, 0, ids);
                dispId = ids[0];
            }
            catch (Exception ex)
            {
                Fail("GetCurveInfo não existe nesta versão: " + ex.Message);
                return null;
            }

            // VARIANT is 16 bytes on x86 and 24 on x64; allocate generously and zero it.
            IntPtr variant = Marshal.AllocCoTaskMem(32);
            try
            {
                for (int i = 0; i < 32; i++) Marshal.WriteByte(variant, i, 0);

                var noArgs = new DISPPARAMS();
                int hr = dispatch.Invoke(dispId, ref _iidNull, 0,
                                         DispatchMethod | DispatchPropertyGet,
                                         ref noArgs, variant, IntPtr.Zero, IntPtr.Zero);
                if (hr < 0)
                {
                    Fail("Invoke(GetCurveInfo) devolveu 0x" + hr.ToString("x8"));
                    return null;
                }

                ushort vt = (ushort)Marshal.ReadInt16(variant, 0);
                if (vt != VtArrayRecord)
                {
                    Fail("GetCurveInfo devolveu VARIANT tipo 0x" + vt.ToString("x4")
                       + ", esperado 0x" + VtArrayRecord.ToString("x4"));
                    return null;
                }

                // The SAFEARRAY pointer lives in the VARIANT union: offset 8 on both architectures.
                IntPtr psa = Marshal.ReadIntPtr(variant, 8);
                if (psa == IntPtr.Zero) return new List<CurvePoint>();

                return Decode(psa);
            }
            finally
            {
                try { VariantClear(variant); } catch (Exception) { }
                Marshal.FreeCoTaskMem(variant);
            }
        }

        private List<CurvePoint>? Decode(IntPtr psa)
        {
            uint dims = SafeArrayGetDim(psa);
            if (dims != 1) { Fail("SAFEARRAY com " + dims + " dimensões, esperado 1"); return null; }

            uint elementSize = SafeArrayGetElemsize(psa);
            if (elementSize != ExpectedElementSize && elementSize != AlternateElementSize)
            {
                // Refusing here is the whole point of the class: decoding an unexpected layout would
                // produce coordinates that look plausible and are wrong, and those coordinates decide
                // which pieces of the customer's drawing get replaced.
                Fail("tamanho de elemento inesperado: " + elementSize + " bytes");
                return null;
            }

            if (SafeArrayGetLBound(psa, 1, out int lower) < 0) { Fail("SafeArrayGetLBound falhou"); return null; }
            if (SafeArrayGetUBound(psa, 1, out int upper) < 0) { Fail("SafeArrayGetUBound falhou"); return null; }

            int count = upper - lower + 1;
            if (count <= 0) return new List<CurvePoint>();

            if (SafeArrayAccessData(psa, out IntPtr data) < 0)
            {
                Fail("SafeArrayAccessData falhou");
                return null;
            }

            try
            {
                var points = new List<CurvePoint>(count);
                for (int i = 0; i < count; i++)
                {
                    long at = i * (long)elementSize;
                    points.Add(new CurvePoint
                    {
                        X = ReadDouble(data, at),
                        Y = ReadDouble(data, at + 8),
                        ElementType = Marshal.ReadInt32(data, (int)(at + 16)),
                        NodeType = Marshal.ReadInt32(data, (int)(at + 20)),
                    });
                }
                return points;
            }
            finally
            {
                try { SafeArrayUnaccessData(psa); } catch (Exception) { }
            }
        }

        private static double ReadDouble(IntPtr data, long offset) =>
            BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, (int)offset));

        private static object? GetProperty(object target, string name)
        {
            try
            {
                return target.GetType().InvokeMember(
                    name, System.Reflection.BindingFlags.GetProperty, null, target, null);
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Records a failure once and stops trying. Whether curve data can be read is a property of the
        /// CorelDRAW BUILD, not of the shape — retrying it 14.342 times cost 29 seconds and a 1,28 MB log
        /// the first time this lesson went unlearned.
        /// </summary>
        private void Fail(string reason)
        {
            _unsupported = true;
            if (_logged) return;
            _logged = true;
            _log?.Invoke("CurveInfo indisponível (não será tentado de novo): " + reason);
        }
    }
}
