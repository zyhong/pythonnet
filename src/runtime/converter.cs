using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Security;
using System.Collections;

namespace Python.Runtime
{
    /// <summary>
    /// Performs data conversions between managed types and Python types.
    /// </summary>
    [SuppressUnmanagedCodeSecurity]
    internal class Converter
    {
        private Converter()
        {
        }

        private static NumberFormatInfo nfi;
        private static Type objectType;
        private static Type stringType;
        private static Type singleType;
        private static Type doubleType;
        private static Type decimalType;
        private static Type int16Type;
        private static Type int32Type;
        private static Type int64Type;
        private static Type flagsType;
        private static Type boolType;
        private static Type typeType;

        static Converter()
        {
            nfi = NumberFormatInfo.InvariantInfo;
            objectType = typeof(Object);
            stringType = typeof(String);
            int16Type = typeof(Int16);
            int32Type = typeof(Int32);
            int64Type = typeof(Int64);
            singleType = typeof(Single);
            doubleType = typeof(Double);
            decimalType = typeof(Decimal);
            flagsType = typeof(FlagsAttribute);
            boolType = typeof(Boolean);
            typeType = typeof(Type);
        }


        /// <summary>
        /// Given a builtin Python type, return the corresponding CLR type.
        /// </summary>
        internal static Type GetTypeByAlias(IntPtr op)
        {
            if (op == Runtime.PyStringType ||
                op == Runtime.PyUnicodeType)
            {
                return stringType;
            }
            else if (op == Runtime.PyIntType)
            {
                return int32Type;
            }
            else if (op == Runtime.PyLongType)
            {
                return int64Type;
            }
            else if (op == Runtime.PyFloatType)
            {
                return doubleType;
            }
            else if (op == Runtime.PyBoolType)
            {
                return boolType;
            }
            return null;
        }

        internal static IntPtr GetPythonTypeByAlias(Type op)
        {
            if (op == stringType)
            {
                return Runtime.PyUnicodeType;
            }

            else if (Runtime.IsPython3 && (op == int16Type ||
                                           op == int32Type ||
                                           op == int64Type))
            {
                return Runtime.PyIntType;
            }

            else if (op == int16Type ||
                     op == int32Type)
            {
                return Runtime.PyIntType;
            }
            else if (op == int64Type)
            {
                return Runtime.PyLongType;
            }
            else if (op == doubleType ||
                     op == singleType)
            {
                return Runtime.PyFloatType;
            }
            else if (op == boolType)
            {
                return Runtime.PyBoolType;
            }
            return IntPtr.Zero;
        }


        /// <summary>
        /// Return a Python object for the given native object, converting
        /// basic types (string, int, etc.) into equivalent Python objects.
        /// This always returns a new reference. Note that the System.Decimal
        /// type has no Python equivalent and converts to a managed instance.
        /// </summary>
        internal static IntPtr ToPython<T>(T value)
        {
            return ToPython(value, typeof(T));
        }

        internal static IntPtr ToPython(object value, Type type)
        {
            if (value is PyObject)
            {
                IntPtr handle = ((PyObject)value).Handle;
                Runtime.XIncref(handle);
                return handle;
            }
            IntPtr result = IntPtr.Zero;

            // Null always converts to None in Python.

            if (value == null)
            {
                result = Runtime.PyNone;
                Runtime.XIncref(result);
                return result;
            }

            // it the type is a python subclass of a managed type then return the
            // underlying python object rather than construct a new wrapper object.
            var pyderived = value as IPythonDerivedType;
            if (null != pyderived)
            {
                return ClassDerivedObject.ToPython(pyderived);
            }

            // hmm - from Python, we almost never care what the declared
            // type is. we'd rather have the object bound to the actual
            // implementing class.

            type = value.GetType();

            TypeCode tc = Type.GetTypeCode(type);

            switch (tc)
            {
                case TypeCode.Object:
                    return CLRObject.GetInstHandle(value, type);

                case TypeCode.String:
                    return Runtime.PyPyUnicode_FromString((string)value);

                case TypeCode.Int32:
                    return Runtime.PyPyInt_FromInt32((int)value);

                case TypeCode.Boolean:
                    if ((bool)value)
                    {
                        Runtime.XIncref(Runtime.PyTrue);
                        return Runtime.PyTrue;
                    }
                    Runtime.XIncref(Runtime.PyFalse);
                    return Runtime.PyFalse;

                case TypeCode.Byte:
                    return Runtime.PyPyInt_FromInt32((int)((byte)value));

                case TypeCode.Char:
                    return Runtime.PyPyUnicode_FromOrdinal((int)((char)value));

                case TypeCode.Int16:
                    return Runtime.PyPyInt_FromInt32((int)((short)value));

                case TypeCode.Int64:
                    return Runtime.PyPyLong_FromLongLong((long)value);

                case TypeCode.Single:
                    // return Runtime.PyPyFloat_FromDouble((double)((float)value));
                    string ss = ((float)value).ToString(nfi);
                    IntPtr ps = Runtime.PyPyString_FromString(ss);
                    IntPtr op = Runtime.PyPyFloat_FromString(ps, IntPtr.Zero);
                    Runtime.XDecref(ps);
                    return op;

                case TypeCode.Double:
                    return Runtime.PyPyFloat_FromDouble((double)value);

                case TypeCode.SByte:
                    return Runtime.PyPyInt_FromInt32((int)((sbyte)value));

                case TypeCode.UInt16:
                    return Runtime.PyPyInt_FromInt32((int)((ushort)value));

                case TypeCode.UInt32:
                    return Runtime.PyPyLong_FromUnsignedLong((uint)value);

                case TypeCode.UInt64:
                    return Runtime.PyPyLong_FromUnsignedLongLong((ulong)value);

                default:
                    if (value is IEnumerable)
                    {
                        using (var resultlist = new PyList())
                        {
                            foreach (object o in (IEnumerable)value)
                            {
                                using (var p = new PyObject(ToPython(o, o?.GetType())))
                                {
                                    resultlist.Append(p);
                                }
                            }
                            Runtime.XIncref(resultlist.Handle);
                            return resultlist.Handle;
                        }
                    }
                    result = CLRObject.GetInstHandle(value, type);
                    return result;
            }
        }


        /// <summary>
        /// In a few situations, we don't have any advisory type information
        /// when we want to convert an object to Python.
        /// </summary>
        internal static IntPtr ToPythonImplicit(object value)
        {
            if (value == null)
            {
                IntPtr result = Runtime.PyNone;
                Runtime.XIncref(result);
                return result;
            }

            return ToPython(value, objectType);
        }


        /// <summary>
        /// Return a managed object for the given Python object, taking funny
        /// byref types into account.
        /// </summary>
        internal static bool ToManaged(IntPtr value, Type type,
            out object result, bool setError)
        {
            if (type.IsByRef)
            {
                type = type.GetElementType();
            }
            return Converter.ToManagedValue(value, type, out result, setError);
        }


        internal static bool ToManagedValue(IntPtr value, Type obType,
            out object result, bool setError)
        {
            if (obType == typeof(PyObject))
            {
                Runtime.XIncref(value); // PyObject() assumes ownership
                result = new PyObject(value);
                return true;
            }

            // Common case: if the Python value is a wrapped managed object
            // instance, just return the wrapped object.
            ManagedType mt = ManagedType.GetManagedObject(value);
            result = null;

            if (mt != null)
            {
                if (mt is CLRObject)
                {
                    object tmp = ((CLRObject)mt).inst;
                    if (obType.IsInstanceOfType(tmp))
                    {
                        result = tmp;
                        return true;
                    }
                    var err = "value cannot be converted to {0}";
                    err = string.Format(err, obType);
                    Exceptions.SetError(Exceptions.TypeError, err);
                    return false;
                }
                if (mt is ClassBase)
                {
                    result = ((ClassBase)mt).type;
                    return true;
                }
                // shouldn't happen
                return false;
            }

            if (value == Runtime.PyNone && !obType.IsValueType)
            {
                result = null;
                return true;
            }

            if (obType.IsArray)
            {
                return ToArray(value, obType, out result, setError);
            }

            if (obType.IsEnum)
            {
                return ToEnum(value, obType, out result, setError);
            }

            // Conversion to 'Object' is done based on some reasonable default
            // conversions (Python string -> managed string, Python int -> Int32 etc.).
            if (obType == objectType)
            {
                if (Runtime.IsStringType(value))
                {
                    return ToPrimitive(value, stringType, out result, setError);
                }

                else if (Runtime.PyPyBool_Check(value))
                {
                    return ToPrimitive(value, boolType, out result, setError);
                }

                else if (Runtime.PyPyInt_Check(value))
                {
                    return ToPrimitive(value, int32Type, out result, setError);
                }

                else if (Runtime.PyPyLong_Check(value))
                {
                    return ToPrimitive(value, int64Type, out result, setError);
                }

                else if (Runtime.PyPyFloat_Check(value))
                {
                    return ToPrimitive(value, doubleType, out result, setError);
                }

                else if (Runtime.PyPySequence_Check(value))
                {
                    return ToArray(value, typeof(object[]), out result, setError);
                }

                if (setError)
                {
                    Exceptions.SetError(Exceptions.TypeError, "value cannot be converted to Object");
                }

                return false;
            }

            // Conversion to 'Type' is done using the same mappings as above for objects.
            if (obType == typeType)
            {
                if (value == Runtime.PyStringType)
                {
                    result = stringType;
                    return true;
                }

                else if (value == Runtime.PyBoolType)
                {
                    result = boolType;
                    return true;
                }

                else if (value == Runtime.PyIntType)
                {
                    result = int32Type;
                    return true;
                }

                else if (value == Runtime.PyLongType)
                {
                    result = int64Type;
                    return true;
                }

                else if (value == Runtime.PyFloatType)
                {
                    result = doubleType;
                    return true;
                }

                else if (value == Runtime.PyListType || value == Runtime.PyTupleType)
                {
                    result = typeof(object[]);
                    return true;
                }

                if (setError)
                {
                    Exceptions.SetError(Exceptions.TypeError, "value cannot be converted to Type");
                }

                return false;
            }

            return ToPrimitive(value, obType, out result, setError);
        }

        /// <summary>
        /// Convert a Python value to an instance of a primitive managed type.
        /// </summary>
        private static bool ToPrimitive(IntPtr value, Type obType, out object result, bool setError)
        {
            IntPtr overflow = Exceptions.OverflowError;
            TypeCode tc = Type.GetTypeCode(obType);
            result = null;
            IntPtr op;
            int ival;

            switch (tc)
            {
                case TypeCode.String:
                    string st = Runtime.GetManagedString(value);
                    if (st == null)
                    {
                        goto type_error;
                    }
                    result = st;
                    return true;

                case TypeCode.Int32:
                    // Trickery to support 64-bit platforms.
                    if (Runtime.IsPython2 && Runtime.Is32Bit)
                    {
                        op = Runtime.PyPyNumber_Int(value);

                        // As of Python 2.3, large ints magically convert :(
                        if (Runtime.PyPyLong_Check(op))
                        {
                            Runtime.XDecref(op);
                            goto overflow;
                        }

                        if (op == IntPtr.Zero)
                        {
                            if (Exceptions.ExceptionMatches(overflow))
                            {
                                goto overflow;
                            }
                            goto type_error;
                        }
                        ival = (int)Runtime.PyPyInt_AsLong(op);
                        Runtime.XDecref(op);
                        result = ival;
                        return true;
                    }
                    else // Python3 always use PyLong API
                    {
                        op = Runtime.PyPyNumber_Long(value);
                        if (op == IntPtr.Zero)
                        {
                            Exceptions.Clear();
                            if (Exceptions.ExceptionMatches(overflow))
                            {
                                goto overflow;
                            }
                            goto type_error;
                        }
                        long ll = (long)Runtime.PyPyLong_AsLongLong(op);
                        Runtime.XDecref(op);
                        if (ll == -1 && Exceptions.ErrorOccurred())
                        {
                            goto overflow;
                        }
                        if (ll > Int32.MaxValue || ll < Int32.MinValue)
                        {
                            goto overflow;
                        }
                        result = (int)ll;
                        return true;
                    }

                case TypeCode.Boolean:
                    result = Runtime.PyPyObject_IsTrue(value) != 0;
                    return true;

                case TypeCode.Byte:
#if PYTHON3
                    if (Runtime.PyPyObject_TypeCheck(value, Runtime.PyBytesType))
                    {
                        if (Runtime.PyPyBytes_Size(value) == 1)
                        {
                            op = Runtime.PyPyBytes_AS_STRING(value);
                            result = (byte)Marshal.ReadByte(op);
                            return true;
                        }
                        goto type_error;
                    }
#elif PYTHON2
                    if (Runtime.PyPyObject_TypeCheck(value, Runtime.PyStringType))
                    {
                        if (Runtime.PyPyString_Size(value) == 1)
                        {
                            op = Runtime.PyPyString_AS_STRING(value);
                            result = (byte)Marshal.ReadByte(op);
                            return true;
                        }
                        goto type_error;
                    }
#endif

                    op = Runtime.PyPyNumber_Int(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    ival = (int)Runtime.PyPyInt_AsLong(op);
                    Runtime.XDecref(op);

                    if (ival > Byte.MaxValue || ival < Byte.MinValue)
                    {
                        goto overflow;
                    }
                    byte b = (byte)ival;
                    result = b;
                    return true;

                case TypeCode.SByte:
#if PYTHON3
                    if (Runtime.PyPyObject_TypeCheck(value, Runtime.PyBytesType))
                    {
                        if (Runtime.PyPyBytes_Size(value) == 1)
                        {
                            op = Runtime.PyPyBytes_AS_STRING(value);
                            result = (byte)Marshal.ReadByte(op);
                            return true;
                        }
                        goto type_error;
                    }
#elif PYTHON2
                    if (Runtime.PyPyObject_TypeCheck(value, Runtime.PyStringType))
                    {
                        if (Runtime.PyPyString_Size(value) == 1)
                        {
                            op = Runtime.PyPyString_AS_STRING(value);
                            result = (sbyte)Marshal.ReadByte(op);
                            return true;
                        }
                        goto type_error;
                    }
#endif

                    op = Runtime.PyPyNumber_Int(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    ival = (int)Runtime.PyPyInt_AsLong(op);
                    Runtime.XDecref(op);

                    if (ival > SByte.MaxValue || ival < SByte.MinValue)
                    {
                        goto overflow;
                    }
                    sbyte sb = (sbyte)ival;
                    result = sb;
                    return true;

                case TypeCode.Char:
#if PYTHON3
                    if (Runtime.PyPyObject_TypeCheck(value, Runtime.PyBytesType))
                    {
                        if (Runtime.PyPyBytes_Size(value) == 1)
                        {
                            op = Runtime.PyPyBytes_AS_STRING(value);
                            result = (byte)Marshal.ReadByte(op);
                            return true;
                        }
                        goto type_error;
                    }
#elif PYTHON2
                    if (Runtime.PyPyObject_TypeCheck(value, Runtime.PyStringType))
                    {
                        if (Runtime.PyPyString_Size(value) == 1)
                        {
                            op = Runtime.PyPyString_AS_STRING(value);
                            result = (char)Marshal.ReadByte(op);
                            return true;
                        }
                        goto type_error;
                    }
#endif
                    else if (Runtime.PyPyObject_TypeCheck(value, Runtime.PyUnicodeType))
                    {
                        if (Runtime.PyPyUnicode_GetSize(value) == 1)
                        {
                            op = Runtime.PyPyUnicode_AS_UNICODE(value);
                            if (Runtime.UCS == 2) // Don't trust linter, statement not always true.
                            {
                                // 2011-01-02: Marshal as character array because the cast
                                // result = (char)Marshal.ReadInt16(op); throws an OverflowException
                                // on negative numbers with Check Overflow option set on the project
                                Char[] buff = new Char[1];
                                Marshal.Copy(op, buff, 0, 1);
                                result = buff[0];
                            }
                            else // UCS4
                            {
                                // XXX this is probably NOT correct?
                                result = (char)Marshal.ReadInt32(op);
                            }
                            return true;
                        }
                        goto type_error;
                    }

                    op = Runtime.PyPyNumber_Int(value);
                    if (op == IntPtr.Zero)
                    {
                        goto type_error;
                    }
                    ival = Runtime.PyPyInt_AsLong(op);
                    Runtime.XDecref(op);
                    if (ival > Char.MaxValue || ival < Char.MinValue)
                    {
                        goto overflow;
                    }
                    result = (char)ival;
                    return true;

                case TypeCode.Int16:
                    op = Runtime.PyPyNumber_Int(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    ival = (int)Runtime.PyPyInt_AsLong(op);
                    Runtime.XDecref(op);
                    if (ival > Int16.MaxValue || ival < Int16.MinValue)
                    {
                        goto overflow;
                    }
                    short s = (short)ival;
                    result = s;
                    return true;

                case TypeCode.Int64:
                    op = Runtime.PyPyNumber_Long(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    long l = (long)Runtime.PyPyLong_AsLongLong(op);
                    Runtime.XDecref(op);
                    if ((l == -1) && Exceptions.ErrorOccurred())
                    {
                        goto overflow;
                    }
                    result = l;
                    return true;

                case TypeCode.UInt16:
                    op = Runtime.PyPyNumber_Int(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    ival = (int)Runtime.PyPyInt_AsLong(op);
                    Runtime.XDecref(op);
                    if (ival > UInt16.MaxValue || ival < UInt16.MinValue)
                    {
                        goto overflow;
                    }
                    ushort us = (ushort)ival;
                    result = us;
                    return true;

                case TypeCode.UInt32:
                    op = Runtime.PyPyNumber_Long(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    uint ui = (uint)Runtime.PyPyLong_AsUnsignedLong(op);

                    if (Exceptions.ErrorOccurred())
                    {
                        Runtime.XDecref(op);
                        goto overflow;
                    }

                    IntPtr check = Runtime.PyPyLong_FromUnsignedLong(ui);
                    int err = Runtime.PyPyObject_Compare(check, op);
                    Runtime.XDecref(check);
                    Runtime.XDecref(op);
                    if (0 != err || Exceptions.ErrorOccurred())
                    {
                        goto overflow;
                    }

                    result = ui;
                    return true;

                case TypeCode.UInt64:
                    op = Runtime.PyPyNumber_Long(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    ulong ul = (ulong)Runtime.PyPyLong_AsUnsignedLongLong(op);
                    Runtime.XDecref(op);
                    if (Exceptions.ErrorOccurred())
                    {
                        goto overflow;
                    }
                    result = ul;
                    return true;


                case TypeCode.Single:
                    op = Runtime.PyPyNumber_Float(value);
                    if (op == IntPtr.Zero)
                    {
                        if (Exceptions.ExceptionMatches(overflow))
                        {
                            goto overflow;
                        }
                        goto type_error;
                    }
                    double dd = Runtime.PyPyFloat_AsDouble(op);
                    Runtime.XDecref(op);
                    if (dd > Single.MaxValue || dd < Single.MinValue)
                    {
                        goto overflow;
                    }
                    result = (float)dd;
                    return true;

                case TypeCode.Double:
                    op = Runtime.PyPyNumber_Float(value);
                    if (op == IntPtr.Zero)
                    {
                        goto type_error;
                    }
                    double d = Runtime.PyPyFloat_AsDouble(op);
                    Runtime.XDecref(op);
                    if (d > Double.MaxValue || d < Double.MinValue)
                    {
                        goto overflow;
                    }
                    result = d;
                    return true;
            }


            type_error:

            if (setError)
            {
                var format = "'{0}' value cannot be converted to {1}";
                string tpName = Runtime.PyPyObject_GetTypeName(value);
                string error = string.Format(format, tpName, obType);
                Exceptions.SetError(Exceptions.TypeError, error);
            }

            return false;

            overflow:

            if (setError)
            {
                var error = "value too large to convert";
                Exceptions.SetError(Exceptions.OverflowError, error);
            }

            return false;
        }


        private static void SetConversionError(IntPtr value, Type target)
        {
            IntPtr ob = Runtime.PyPyObject_Repr(value);
            string src = Runtime.GetManagedString(ob);
            Runtime.XDecref(ob);
            string error = string.Format("Cannot convert {0} to {1}", src, target);
            Exceptions.SetError(Exceptions.TypeError, error);
        }


        /// <summary>
        /// Convert a Python value to a correctly typed managed array instance.
        /// The Python value must support the Python sequence protocol and the
        /// items in the sequence must be convertible to the target array type.
        /// </summary>
        private static bool ToArray(IntPtr value, Type obType, out object result, bool setError)
        {
            Type elementType = obType.GetElementType();
            int size = Runtime.PyPySequence_Size(value);
            result = null;

            if (size < 0)
            {
                if (setError)
                {
                    SetConversionError(value, obType);
                }
                return false;
            }

            Array items = Array.CreateInstance(elementType, size);

            // XXX - is there a better way to unwrap this if it is a real array?
            for (var i = 0; i < size; i++)
            {
                object obj = null;
                IntPtr item = Runtime.PyPySequence_GetItem(value, i);
                if (item == IntPtr.Zero)
                {
                    if (setError)
                    {
                        SetConversionError(value, obType);
                        return false;
                    }
                }

                if (!Converter.ToManaged(item, elementType, out obj, true))
                {
                    Runtime.XDecref(item);
                    return false;
                }

                items.SetValue(obj, i);
                Runtime.XDecref(item);
            }

            result = items;
            return true;
        }


        /// <summary>
        /// Convert a Python value to a correctly typed managed enum instance.
        /// </summary>
        private static bool ToEnum(IntPtr value, Type obType, out object result, bool setError)
        {
            Type etype = Enum.GetUnderlyingType(obType);
            result = null;

            if (!ToPrimitive(value, etype, out result, setError))
            {
                return false;
            }

            if (Enum.IsDefined(obType, result))
            {
                result = Enum.ToObject(obType, result);
                return true;
            }

            if (obType.GetCustomAttributes(flagsType, true).Length > 0)
            {
                result = Enum.ToObject(obType, result);
                return true;
            }

            if (setError)
            {
                var error = "invalid enumeration value";
                Exceptions.SetError(Exceptions.ValueError, error);
            }

            return false;
        }
    }

    public static class ConverterExtension
    {
        public static PyObject ToPython(this object o)
        {
            return new PyObject(Converter.ToPython(o, o?.GetType()));
        }
    }
}
