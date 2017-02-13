using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace Python.Runtime
{
    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
#if MONO_LINUX || MONO_OSX
        private static int RTLD_NOW = 0x2;
        private static int RTLD_SHARED = 0x20;
#if MONO_OSX
        private static IntPtr RTLD_DEFAULT = new IntPtr(-2);
        private const string NativeDll = "__Internal";
#elif MONO_LINUX
        private static IntPtr RTLD_DEFAULT = IntPtr.Zero;
        private const string NativeDll = "libdl.so";
#endif

        public static IntPtr LoadLibrary(string fileName)
        {
            return dlopen(fileName, RTLD_NOW | RTLD_SHARED);
        }

        public static void FreeLibrary(IntPtr handle)
        {
            dlclose(handle);
        }

        public static IntPtr GetProcAddress(IntPtr dllHandle, string name)
        {
            // look in the exe if dllHandle is NULL
            if (dllHandle == IntPtr.Zero)
            {
                dllHandle = RTLD_DEFAULT;
            }

            // clear previous errors if any
            dlerror();
            IntPtr res = dlsym(dllHandle, name);
            IntPtr errPtr = dlerror();
            if (errPtr != IntPtr.Zero)
            {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }

        [DllImport(NativeDll)]
        private static extern IntPtr dlopen(String fileName, int flags);

        [DllImport(NativeDll)]
        private static extern IntPtr dlsym(IntPtr handle, String symbol);

        [DllImport(NativeDll)]
        private static extern int dlclose(IntPtr handle);

        [DllImport(NativeDll)]
        private static extern IntPtr dlerror();
#else // Windows
        private const string NativeDll = "kernel32.dll";

        [DllImport(NativeDll)]
        public static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport(NativeDll)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport(NativeDll)]
        public static extern bool FreeLibrary(IntPtr hModule);
#endif
    }

    /// <summary>
    /// Encapsulates the low-level Python C API. Note that it is
    /// the responsibility of the caller to have acquired the GIL
    /// before calling any of these methods.
    /// </summary>
    public class Runtime
    {
#if UCS4
        public const int UCS = 4;
#elif UCS2
        public const int UCS = 2;
#else
#error You must define either UCS2 or UCS4!
#endif

#if PYTHON27
        public const string pyversion = "2.7";
        public const int pyversionnumber = 27;
#elif PYTHON33
        public const string pyversion = "3.3";
        public const int pyversionnumber = 33;
#elif PYTHON34
        public const string pyversion = "3.4";
        public const int pyversionnumber = 34;
#elif PYTHON35
        public const string pyversion = "3.5";
        public const int pyversionnumber = 35;
#elif PYTHON36
        public const string pyversion = "3.6";
        public const int pyversionnumber = 36;
#elif PYTHON37 // TODO: Add interop37 after Python3.7 is released
        public const string pyversion = "3.7";
        public const int pyversionnumber = 37;
#else
#error You must define one of PYTHON33 to PYTHON37 or PYTHON27
#endif

#if MONO_LINUX || MONO_OSX
#if PYTHON27
        internal const string dllBase = "python27";
#elif PYTHON33
        internal const string dllBase = "python3.3";
#elif PYTHON34
        internal const string dllBase = "python3.4";
#elif PYTHON35
        internal const string dllBase = "python3.5";
#elif PYTHON36
        internal const string dllBase = "python3.6";
#elif PYTHON37
        internal const string dllBase = "python3.7";
#endif
#else // Windows
#if PYTHON27
        internal const string dllBase = "python27";
#elif PYTHON33
        internal const string dllBase = "python33";
#elif PYTHON34
        internal const string dllBase = "python34";
#elif PYTHON35
        internal const string dllBase = "python35";
#elif PYTHON36
        internal const string dllBase = "python36";
#elif PYTHON37
        internal const string dllBase = "python37";
#endif
#endif

#if PyPyTHON_WITH_PYDEBUG
        internal const string dllWithPyDebug = "d";
#else
        internal const string dllWithPyDebug = "";
#endif
#if PyPyTHON_WITH_PYMALLOC
        internal const string dllWithPyMalloc = "m";
#else
        internal const string dllWithPyMalloc = "";
#endif
#if PyPyTHON_WITH_WIDE_UNICODE
        internal const string dllWithWideUnicode = "u";
#else
        internal const string dllWithWideUnicode = "";
#endif

#if PyPyTHON_WITHOUT_ENABLE_SHARED
        public const string dll = "__Internal";
#else
        public const string dll = dllBase + dllWithPyDebug + dllWithPyMalloc + dllWithWideUnicode;
#endif

        // set to true when python is finalizing
        internal static Object IsFinalizingLock = new Object();
        internal static bool IsFinalizing = false;

        internal static bool Is32Bit;
        internal static bool IsPython2;
        internal static bool IsPython3;

        /// <summary>
        /// Initialize the runtime...
        /// </summary>
        internal static void Initialize()
        {
            Is32Bit = IntPtr.Size == 4;
            IsPython2 = pyversionnumber < 30;
            IsPython3 = pyversionnumber >= 30;
            try {
                if (Runtime.PyPy_IsInitialized() == 0) {
                    Runtime.PyPy_Initialize();
                }
            }
            catch {

            }

            if (Runtime.PyPyEval_ThreadsInitialized() == 0)
            {
                Runtime.PyPyEval_InitThreads();
            }

            IntPtr op;
            IntPtr dict;
            if (IsPython3)
            {
                op = Runtime.PyPyImport_ImportModule("builtins");
                dict = Runtime.PyPyObject_GetAttrString(op, "__dict__");

            }
            else // Python2
            {
                dict = Runtime.PyPyImport_GetModuleDict();
                op = Runtime.PyPyDict_GetItemString(dict, "__builtin__");
            }
            PyNotImplemented = Runtime.PyPyObject_GetAttrString(op, "NotImplemented");
            PyBaseObjectType = Runtime.PyPyObject_GetAttrString(op, "object");

            PyModuleType = Runtime.PyPyObject_Type(op);
            PyNone = Runtime.PyPyObject_GetAttrString(op, "None");
            PyTrue = Runtime.PyPyObject_GetAttrString(op, "True");
            PyFalse = Runtime.PyPyObject_GetAttrString(op, "False");

            PyBoolType = Runtime.PyPyObject_Type(PyTrue);
            PyNoneType = Runtime.PyPyObject_Type(PyNone);
            PyTypeType = Runtime.PyPyObject_Type(PyNoneType);

            op = Runtime.PyPyObject_GetAttrString(dict, "keys");
            PyMethodType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

            // For some arcane reason, builtins.__dict__.__setitem__ is *not*
            // a wrapper_descriptor, even though dict.__setitem__ is.
            //
            // object.__init__ seems safe, though.
            op = Runtime.PyPyObject_GetAttrString(PyBaseObjectType, "__init__");
            PyWrapperDescriptorType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

#if PYTHON3
            Runtime.XDecref(dict);
#endif

            op = Runtime.PyPyString_FromString("string");
            PyStringType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

            op = Runtime.PyPyUnicode_FromString("unicode");
            PyUnicodeType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

#if PYTHON3
            op = Runtime.PyPyBytes_FromString("bytes");
            PyBytesType = Runtime.PyPyObject_Type(op);
            Runtime.XDecref(op);
#endif

            op = Runtime.PyPyTuple_New(0);
            PyTupleType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

            op = Runtime.PyPyList_New(0);
            PyListType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

            op = Runtime.PyPyDict_New();
            PyDictType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

            op = Runtime.PyPyInt_FromInt32(0);
            PyIntType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

            op = Runtime.PyPyLong_FromLong(0);
            PyLongType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

            op = Runtime.PyPyFloat_FromDouble(0);
            PyFloatType = Runtime.PyPyObject_Type(op);
            //FIXME
            //Runtime.XDecref(op);

#if PYTHON3
            PyClassType = IntPtr.Zero;
            PyInstanceType = IntPtr.Zero;
#elif PYTHON2
            //FIXME 
            /*
            IntPtr s = Runtime.PyPyString_FromString("_temp");
            IntPtr d = Runtime.PyPyDict_New();

            IntPtr c = Runtime.PyPyClass_New(IntPtr.Zero, d, s);
            PyClassType = Runtime.PyPyObject_Type(c);

            IntPtr i = Runtime.PyPyInstance_New(c, IntPtr.Zero, IntPtr.Zero);
            PyInstanceType = Runtime.PyPyObject_Type(i);
            Runtime.XDecref(s);
            Runtime.XDecref(i);
            Runtime.XDecref(c);
            Runtime.XDecref(d);
            */
#endif

            Error = new IntPtr(-1);

#if PYTHON3
            IntPtr dll = IntPtr.Zero;
            if ("__Internal" != Runtime.dll)
            {
                NativeMethods.LoadLibrary(Runtime.dll);
            }
            _PyPyObject_NextNotImplemented = NativeMethods.GetProcAddress(dll, "_PyPyObject_NextNotImplemented");
#if !(MONO_LINUX || MONO_OSX)
            if (IntPtr.Zero != dll)
            {
                NativeMethods.FreeLibrary(dll);
            }
#endif
#endif

            // Initialize modules that depend on the runtime class.
            AssemblyManager.Initialize();
            PyCLRMetaType = MetaType.Initialize();
            Exceptions.Initialize();
            ImportHook.Initialize();

            // Need to add the runtime directory to sys.path so that we
            // can find built-in assemblies like System.Data, et. al.
            string rtdir = RuntimeEnvironment.GetRuntimeDirectory();
            IntPtr path = Runtime.PyPySys_GetObject("path");
            IntPtr item = Runtime.PyPyString_FromString(rtdir);
            Runtime.PyPyList_Append(path, item);
            //FIXME
            //Runtime.XDecref(item);
            AssemblyManager.UpdatePath();
        }

        internal static void Shutdown()
        {
            AssemblyManager.Shutdown();
            Exceptions.Shutdown();
            ImportHook.Shutdown();
            PyPy_Finalize();
        }

        // called *without* the GIL acquired by clr._AtExit
        internal static int AtExit()
        {
            lock (IsFinalizingLock)
            {
                IsFinalizing = true;
            }
            return 0;
        }

        internal static IntPtr PyPy_single_input = (IntPtr)256;
        internal static IntPtr PyPy_file_input = (IntPtr)257;
        internal static IntPtr PyPy_eval_input = (IntPtr)258;

        internal static IntPtr PyBaseObjectType;
        internal static IntPtr PyModuleType;
        internal static IntPtr PyClassType;
        internal static IntPtr PyInstanceType;
        internal static IntPtr PyCLRMetaType;
        internal static IntPtr PyMethodType;
        internal static IntPtr PyWrapperDescriptorType;

        internal static IntPtr PyUnicodeType;
        internal static IntPtr PyStringType;
        internal static IntPtr PyTupleType;
        internal static IntPtr PyListType;
        internal static IntPtr PyDictType;
        internal static IntPtr PyIntType;
        internal static IntPtr PyLongType;
        internal static IntPtr PyFloatType;
        internal static IntPtr PyBoolType;
        internal static IntPtr PyNoneType;
        internal static IntPtr PyTypeType;

#if PYTHON3
        internal static IntPtr PyBytesType;
        internal static IntPtr _PyPyObject_NextNotImplemented;
#endif

        internal static IntPtr PyNotImplemented;
        internal const int PyPy_LT = 0;
        internal const int PyPy_LE = 1;
        internal const int PyPy_EQ = 2;
        internal const int PyPy_NE = 3;
        internal const int PyPy_GT = 4;
        internal const int PyPy_GE = 5;

        internal static IntPtr PyTrue;
        internal static IntPtr PyFalse;
        internal static IntPtr PyNone;
        internal static IntPtr Error;

        internal static IntPtr GetBoundArgTuple(IntPtr obj, IntPtr args)
        {
            if (Runtime.PyPyObject_TYPE(args) != Runtime.PyTupleType)
            {
                Exceptions.SetError(Exceptions.TypeError, "tuple expected");
                return IntPtr.Zero;
            }
            int size = Runtime.PyPyTuple_Size(args);
            IntPtr items = Runtime.PyPyTuple_New(size + 1);
            Runtime.PyPyTuple_SetItem(items, 0, obj);
            Runtime.XIncref(obj);

            for (int i = 0; i < size; i++)
            {
                IntPtr item = Runtime.PyPyTuple_GetItem(args, i);
                Runtime.XIncref(item);
                Runtime.PyPyTuple_SetItem(items, i + 1, item);
            }

            return items;
        }


        internal static IntPtr ExtendTuple(IntPtr t, params IntPtr[] args)
        {
            int size = Runtime.PyPyTuple_Size(t);
            int add = args.Length;
            IntPtr item;

            IntPtr items = Runtime.PyPyTuple_New(size + add);
            for (int i = 0; i < size; i++)
            {
                item = Runtime.PyPyTuple_GetItem(t, i);
                Runtime.XIncref(item);
                Runtime.PyPyTuple_SetItem(items, i, item);
            }

            for (int n = 0; n < add; n++)
            {
                item = args[n];
                Runtime.XIncref(item);
                Runtime.PyPyTuple_SetItem(items, size + n, item);
            }

            return items;
        }

        internal static Type[] PythonArgsToTypeArray(IntPtr arg)
        {
            return PythonArgsToTypeArray(arg, false);
        }

        internal static Type[] PythonArgsToTypeArray(IntPtr arg, bool mangleObjects)
        {
            // Given a PyObject * that is either a single type object or a
            // tuple of (managed or unmanaged) type objects, return a Type[]
            // containing the CLR Type objects that map to those types.
            IntPtr args = arg;
            bool free = false;

            if (!Runtime.PyPyTuple_Check(arg))
            {
                args = Runtime.PyPyTuple_New(1);
                Runtime.XIncref(arg);
                Runtime.PyPyTuple_SetItem(args, 0, arg);
                free = true;
            }

            int n = Runtime.PyPyTuple_Size(args);
            Type[] types = new Type[n];
            Type t = null;

            for (int i = 0; i < n; i++)
            {
                IntPtr op = Runtime.PyPyTuple_GetItem(args, i);
                if (mangleObjects && (!Runtime.PyPyType_Check(op)))
                {
                    op = Runtime.PyPyObject_TYPE(op);
                }
                ManagedType mt = ManagedType.GetManagedObject(op);

                if (mt is ClassBase)
                {
                    t = ((ClassBase)mt).type;
                }
                else if (mt is CLRObject)
                {
                    object inst = ((CLRObject)mt).inst;
                    if (inst is Type)
                    {
                        t = inst as Type;
                    }
                }
                else
                {
                    t = Converter.GetTypeByAlias(op);
                }

                if (t == null)
                {
                    types = null;
                    break;
                }
                types[i] = t;
            }
            if (free)
            {
                Runtime.XDecref(args);
            }
            return types;
        }

        /// <summary>
        /// Managed exports of the Python C API. Where appropriate, we do
        /// some optimization to avoid managed &lt;--&gt; unmanaged transitions
        /// (mostly for heavily used methods).
        /// </summary>
        internal unsafe static void XIncref(IntPtr op)
        {
//#if PyPy_DEBUG
            // according to Python doc, PyPy_IncRef() is PyPy_XINCREF()
            PyPy_IncRef(op);
            return;
/*#else
            void* p = (void*)op;
            if ((void*)0 != p)
            {
                if (Is32Bit)
                {
                    (*(int*)p)++;
                }
                else
                {
                    (*(long*)p)++;
                }
            }
#endif*/
        }

        internal static unsafe void XDecref(IntPtr op)
        {
//#if PyPy_DEBUG
            // PyPy_DecRef calls Python's PyPy_DECREF
            // according to Python doc, PyPy_DecRef() is PyPy_XDECREF()
            PyPy_DecRef(op);
            return;
/*#else
            void* p = (void*)op;
            if ((void*)0 != p)
            {
                if (Is32Bit)
                {
                    --(*(int*)p);
                }
                else
                {
                    --(*(long*)p);
                }
                if ((*(int*)p) == 0)
                {
                    // PyPyObject_HEAD: struct _typeobject *ob_type
                    void* t = Is32Bit
                        ? (void*)(*((uint*)p + 1))
                        : (void*)(*((ulong*)p + 1));
                    // PyTypeObject: destructor tp_dealloc
                    void* f = Is32Bit
                        ? (void*)(*((uint*)t + 6))
                        : (void*)(*((ulong*)t + 6));
                    if ((void*)0 == f)
                    {
                        return;
                    }
                    NativeCall.Impl.Void_Call_1(new IntPtr(f), op);
                    return;
                }
            }
#endif*/
        }

        internal unsafe static long Refcount(IntPtr op)
        {
            void* p = (void*)op;
            if ((void*)0 != p)
            {
                if (Is32Bit)
                {
                    return (*(int*)p);
                }
                else
                {
                    return (*(long*)p);
                }
            }
            return 0;
        }

//#if PyPy_DEBUG
        // PyPy_IncRef and PyPy_DecRef are taking care of the extra payload
        // in PyPy_DEBUG builds of Python like _PyPy_RefTotal
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        private unsafe static extern void
            PyPy_IncRef(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        private unsafe static extern void
            PyPy_DecRef(IntPtr ob);
//#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_Initialize();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPy_IsInitialized();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_Finalize();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPy_NewInterpreter();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_EndInterpreter(IntPtr threadState);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyThreadState_New(IntPtr istate);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyThreadState_Get();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyThread_get_key_value(IntPtr key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyThread_get_thread_ident();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyThread_set_key_value(IntPtr key, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyThreadState_Swap(IntPtr key);


        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyGILState_Ensure();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyGILState_Release(IntPtr gs);


        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyGILState_GetThisThreadState();

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        public unsafe static extern int
            PyPy_Main(int argc, [MarshalAsAttribute(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] argv);
#elif PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        public unsafe static extern int
            PyPy_Main(int argc, string[] argv);
#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyEval_InitThreads();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyEval_ThreadsInitialized();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyEval_AcquireLock();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyEval_ReleaseLock();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyEval_AcquireThread(IntPtr tstate);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyEval_ReleaseThread(IntPtr tstate);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyEval_SaveThread();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyEval_RestoreThread(IntPtr tstate);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyEval_GetBuiltins();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyEval_GetGlobals();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyEval_GetLocals();


#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.LPWStr)]
        internal unsafe static extern string
            PyPy_GetProgramName();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_SetProgramName([MarshalAsAttribute(UnmanagedType.LPWStr)] string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.LPWStr)]
        internal unsafe static extern string
            PyPy_GetPythonHome();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_SetPythonHome([MarshalAsAttribute(UnmanagedType.LPWStr)] string home);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.LPWStr)]
        internal unsafe static extern string
            PyPy_GetPath();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_SetPath([MarshalAsAttribute(UnmanagedType.LPWStr)] string home);
#elif PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetProgramName();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_SetProgramName(string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetPythonHome();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_SetPythonHome(string home);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetPath();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPy_SetPath(string home);
#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetVersion();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetPlatform();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetCopyright();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetCompiler();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPy_GetBuildInfo();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyRun_SimpleString(string code);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyRun_String(string code, IntPtr st, IntPtr globals, IntPtr locals);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPy_CompileString(string code, string file, IntPtr tok);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyImport_ExecCodeModule(string name, IntPtr code);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyCFunction_NewEx(IntPtr ml, IntPtr self, IntPtr mod);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyCFunction_Call(IntPtr func, IntPtr args, IntPtr kw);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyClass_New(IntPtr bases, IntPtr dict, IntPtr name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyInstance_New(IntPtr cls, IntPtr args, IntPtr kw);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyInstance_NewRaw(IntPtr cls, IntPtr dict);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyMethod_New(IntPtr func, IntPtr self, IntPtr cls);


        //====================================================================
        // Python abstract object API
        //====================================================================

        /// <summary>
        /// A macro-like method to get the type of a Python object. This is
        /// designed to be lean and mean in IL &amp; avoid managed &lt;-&gt; unmanaged
        /// transitions. Note that this does not incref the type object.
        /// </summary>
        /*internal unsafe static IntPtr
            PyPyObject_TYPE(IntPtr op)
        {
            void* p = (void*)op;
            if ((void*)0 == p)
            {
                return IntPtr.Zero;
            }
#if PyPy_DEBUG
            int n = 3;
#else
            int n = 1;
#endif
            if (Is32Bit)
            {
                return new IntPtr((void*)(*((uint*)p + n)));
            }
            else
            {
                return new IntPtr((void*)(*((ulong*)p + n)));
            }
        }*/

        /// <summary>
        /// Managed version of the standard Python C API PyPyObject_Type call.
        /// This version avoids a managed  &lt;-&gt; unmanaged transition. This one
        /// does incref the returned type object.
        /// </summary>
        /// <summary>
        /// Managed version of the standard Python C API PyObject_Type call.
        /// This version avoids a managed  &lt;-&gt; unmanaged transition. This one
        /// does incref the returned type object.
        /// </summary>
        internal unsafe static IntPtr
            PyPyObject_TYPE(IntPtr op)
        {
            IntPtr tp = PyPyObject_Type(op);
            //FIXME: leaks tp 
            //Runtime.XIncref(tp);
            return tp;
        }

        internal static string PyPyObject_GetTypeName(IntPtr op)
        {
            IntPtr pyType = Marshal.ReadIntPtr(op, ObjectOffset.ob_type);
            IntPtr ppName = Marshal.ReadIntPtr(pyType, TypeOffset.tp_name);
            return Marshal.PtrToStringAnsi(ppName);
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_HasAttrString(IntPtr pointer, string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_GetAttrString(IntPtr pointer, string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_SetAttrString(IntPtr pointer, string name, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_HasAttr(IntPtr pointer, IntPtr name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_GetAttr(IntPtr pointer, IntPtr name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_SetAttr(IntPtr pointer, IntPtr name, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_GetItem(IntPtr pointer, IntPtr key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_SetItem(IntPtr pointer, IntPtr key, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_DelItem(IntPtr pointer, IntPtr key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_GetIter(IntPtr op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_Call(IntPtr pointer, IntPtr args, IntPtr kw);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_CallObject(IntPtr pointer, IntPtr args);

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_RichCompareBool(IntPtr value1, IntPtr value2, int opid);

        internal static int PyPyObject_Compare(IntPtr value1, IntPtr value2)
        {
            int res;
            res = PyPyObject_RichCompareBool(value1, value2, PyPy_LT);
            if (-1 == res)
                return -1;
            else if (1 == res)
                return -1;

            res = PyPyObject_RichCompareBool(value1, value2, PyPy_EQ);
            if (-1 == res)
                return -1;
            else if (1 == res)
                return 0;

            res = PyPyObject_RichCompareBool(value1, value2, PyPy_GT);
            if (-1 == res)
                return -1;
            else if (1 == res)
                return 1;

            Exceptions.SetError(Exceptions.SystemError, "Error comparing objects");
            return -1;
        }
#elif PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_Compare(IntPtr value1, IntPtr value2);
#endif


        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_IsInstance(IntPtr ob, IntPtr type);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_IsSubclass(IntPtr ob, IntPtr type);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyCallable_Check(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_IsTrue(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_Not(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_Size(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_Hash(IntPtr op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_Repr(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_Str(IntPtr pointer);

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyObject_Str",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_Unicode(IntPtr pointer);
#elif PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_Unicode(IntPtr pointer);
#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_Dir(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
        PyPyObject_Type(IntPtr pointer);

        //====================================================================
        // Python number API
        //====================================================================

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyNumber_Long",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Int(IntPtr ob);
#elif PYTHON2

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Int(IntPtr ob);
#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Long(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Float(IntPtr ob);


        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern bool
            PyPyNumber_Check(IntPtr ob);


        internal static bool PyPyInt_Check(IntPtr ob)
        {
            return PyPyObject_TypeCheck(ob, Runtime.PyIntType);
        }

        internal static bool PyPyBool_Check(IntPtr ob)
        {
            return PyPyObject_TypeCheck(ob, Runtime.PyBoolType);
        }

        internal static IntPtr PyPyInt_FromInt32(int value)
        {
            IntPtr v = new IntPtr(value);
            return PyPyInt_FromLong(v);
        }

        internal static IntPtr PyPyInt_FromInt64(long value)
        {
            IntPtr v = new IntPtr(value);
            return PyPyInt_FromLong(v);
        }

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyLong_FromLong",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        private unsafe static extern IntPtr
            PyPyInt_FromLong(IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyLong_AsLong",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyInt_AsLong(IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyLong_FromString",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyInt_FromString(string value, IntPtr end, int radix);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyLong_GetMax",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyInt_GetMax();
#elif PYTHON2

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        private unsafe static extern IntPtr
            PyPyInt_FromLong(IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyInt_AsLong(IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyInt_FromString(string value, IntPtr end, int radix);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyInt_GetMax();
#endif

        internal static bool PyPyLong_Check(IntPtr ob)
        {
            return PyPyObject_TYPE(ob) == Runtime.PyLongType;
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyLong_FromLong(long value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyLong_FromUnsignedLong(uint value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyLong_FromDouble(double value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyLong_FromLongLong(long value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyLong_FromUnsignedLongLong(ulong value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyLong_FromString(string value, IntPtr end, int radix);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyLong_AsLong(IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern uint
            PyPyLong_AsUnsignedLong(IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern long
            PyPyLong_AsLongLong(IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern ulong
            PyPyLong_AsUnsignedLongLong(IntPtr value);


        internal static bool PyPyFloat_Check(IntPtr ob)
        {
            return PyPyObject_TYPE(ob) == Runtime.PyFloatType;
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyFloat_FromDouble(double value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyFloat_FromString(IntPtr value, IntPtr junk);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern double
            PyPyFloat_AsDouble(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Add(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Subtract(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Multiply(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Divide(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_And(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Xor(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Or(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Lshift(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Rshift(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Power(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Remainder(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceAdd(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceSubtract(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceMultiply(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceDivide(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceAnd(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceXor(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceOr(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceLshift(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceRshift(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlacePower(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_InPlaceRemainder(IntPtr o1, IntPtr o2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Negative(IntPtr o1);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Positive(IntPtr o1);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyNumber_Invert(IntPtr o1);

        //====================================================================
        // Python sequence API
        //====================================================================

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern bool
            PyPySequence_Check(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPySequence_GetItem(IntPtr pointer, int index);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_SetItem(IntPtr pointer, int index, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_DelItem(IntPtr pointer, int index);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPySequence_GetSlice(IntPtr pointer, int i1, int i2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_SetSlice(IntPtr pointer, int i1, int i2, IntPtr v);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_DelSlice(IntPtr pointer, int i1, int i2);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_Size(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_Contains(IntPtr pointer, IntPtr item);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPySequence_Concat(IntPtr pointer, IntPtr other);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPySequence_Repeat(IntPtr pointer, int count);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_Index(IntPtr pointer, IntPtr item);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySequence_Count(IntPtr pointer, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPySequence_Tuple(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPySequence_List(IntPtr pointer);


        //====================================================================
        // Python string API
        //====================================================================

        internal static bool IsStringType(IntPtr op)
        {
            IntPtr t = PyPyObject_TYPE(op);
            return (t == PyStringType) || (t == PyUnicodeType);
        }

        internal static bool PyPyString_Check(IntPtr ob)
        {
            return PyPyObject_TYPE(ob) == Runtime.PyStringType;
        }

        internal static IntPtr PyPyString_FromString(string value)
        {
            return PyPyString_FromStringAndSize(value, value.Length);
        }

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyBytes_FromString(string op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyBytes_Size(IntPtr op);

        internal static IntPtr PyPyBytes_AS_STRING(IntPtr ob)
        {
            return ob + BytesOffset.ob_sval;
        }

        internal static IntPtr PyPyString_FromStringAndSize(string value, int length)
        {
            // copy the string into an unmanaged UTF-8 buffer
            int len = Encoding.UTF8.GetByteCount(value);
            byte[] buffer = new byte[len + 1];
            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
            IntPtr nativeUtf8 = Marshal.AllocHGlobal(buffer.Length);
            try
            {
                Marshal.Copy(buffer, 0, nativeUtf8, buffer.Length);
                return PyPyUnicode_FromStringAndSize(nativeUtf8, length);
            }
            finally
            {
                Marshal.FreeHGlobal(nativeUtf8);
            }
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromStringAndSize(IntPtr value, int size);
#elif PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyString_FromStringAndSize(string value, int size);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyString_AsString",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyString_AS_STRING(IntPtr op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyString_Size(IntPtr pointer);
#endif

        internal static bool PyPyUnicode_Check(IntPtr ob)
        {
            return PyPyObject_TYPE(ob) == Runtime.PyUnicodeType;
        }

#if UCS2 && PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromObject(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromEncodedObject(IntPtr ob, IntPtr enc, IntPtr err);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_FromKindAndData",
            ExactSpelling = true,
            CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromKindAndString(int kind, string s, int size);

        internal static IntPtr PyPyUnicode_FromUnicode(string s, int size)
        {
            return PyPyUnicode_FromKindAndString(2, s, size);
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern int
            PyPyUnicode_GetSize(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern char*
            PyPyUnicode_AsUnicode(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_AsUnicode",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_AS_UNICODE(IntPtr op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromOrdinal(int c);
#elif UCS2 && PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS2_FromObject",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromObject(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS2_FromEncodedObject",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromEncodedObject(IntPtr ob, IntPtr enc, IntPtr err);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_FromUnicode",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromUnicode(string s, int size);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_GetSize",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyUnicode_GetSize(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_AsUnicode",
            ExactSpelling = true)]
        internal unsafe static extern char*
            PyPyUnicode_AsUnicode(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_AsUnicode",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_AS_UNICODE(IntPtr op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_FromOrdinal",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromOrdinal(int c);
#elif UCS4 && PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromObject(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromEncodedObject(IntPtr ob, IntPtr enc, IntPtr err);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_FromKindAndData",
            ExactSpelling = true)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromKindAndString(int kind,
                IntPtr s,
                int size);

        internal static unsafe IntPtr PyPyUnicode_FromKindAndString(int kind,
            string s,
            int size)
        {
            var bufLength = Math.Max(s.Length, size) * 4;

            IntPtr mem = Marshal.AllocHGlobal(bufLength);
            try
            {
                fixed (char* ps = s)
                {
                    Encoding.UTF32.GetBytes(ps, s.Length, (byte*)mem, bufLength);
                }

                var result = PyPyUnicode_FromKindAndString(kind, mem, size);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        internal static IntPtr PyPyUnicode_FromUnicode(string s, int size)
        {
            return PyPyUnicode_FromKindAndString(4, s, size);
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyUnicode_GetSize(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal unsafe static extern IntPtr
            PyPyUnicode_AsUnicode(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicode_AsUnicode",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_AS_UNICODE(IntPtr op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromOrdinal(int c);
#elif UCS4 && PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS4_FromObject",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromObject(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS4_FromEncodedObject",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromEncodedObject(IntPtr ob, IntPtr enc, IntPtr err);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS4_FromUnicode",
            ExactSpelling = true)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromUnicode(IntPtr s, int size);

        internal static unsafe IntPtr PyPyUnicode_FromUnicode(string s, int size)
        {
            var bufLength = Math.Max(s.Length, size) * 4;

            IntPtr mem = Marshal.AllocHGlobal(bufLength);
            try
            {
                fixed (char* ps = s)
                {
                    Encoding.UTF32.GetBytes(ps, s.Length, (byte*)mem, bufLength);
                }

                var result = PyPyUnicode_FromUnicode(mem, size);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS4_GetSize",
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyUnicode_GetSize(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS4_AsUnicode",
            ExactSpelling = true)]
        internal unsafe static extern IntPtr
            PyPyUnicode_AsUnicode(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS4_AsUnicode",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_AS_UNICODE(IntPtr op);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "PyPyUnicodeUCS4_FromOrdinal",
            ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal unsafe static extern IntPtr
            PyPyUnicode_FromOrdinal(int c);
#endif

        internal static IntPtr PyPyUnicode_FromString(string s)
        {
            return PyPyUnicode_FromUnicode(s, (s.Length));
        }

        internal unsafe static string GetManagedString(IntPtr op)
        {
            IntPtr type = PyPyObject_TYPE(op);

#if PYTHON2 // Python 3 strings are all Unicode
            if (type == Runtime.PyStringType)
            {
                return Marshal.PtrToStringAnsi(
                    PyPyString_AS_STRING(op),
                    Runtime.PyPyString_Size(op)
                );
            }
#endif

            if (type == Runtime.PyUnicodeType)
            {
#if UCS4
                IntPtr p = Runtime.PyPyUnicode_AsUnicode(op);
                int length = Runtime.PyPyUnicode_GetSize(op);
                int size = length * 4;
                byte[] buffer = new byte[size];
                Marshal.Copy(p, buffer, 0, size);
                return Encoding.UTF32.GetString(buffer, 0, size);
#elif UCS2
                char* p = Runtime.PyPyUnicode_AsUnicode(op);
                int size = Runtime.PyPyUnicode_GetSize(op);
                return new String(p, 0, size);
#endif
            }

            return null;
        }

        //====================================================================
        // Python dictionary API
        //====================================================================

        internal static bool PyPyDict_Check(IntPtr ob)
        {
            return PyPyObject_TYPE(ob) == Runtime.PyDictType;
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDict_New();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDictProxy_New(IntPtr dict);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDict_GetItem(IntPtr pointer, IntPtr key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDict_GetItemString(IntPtr pointer, string key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyDict_SetItem(IntPtr pointer, IntPtr key, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyDict_SetItemString(IntPtr pointer, string key, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyDict_DelItem(IntPtr pointer, IntPtr key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyDict_DelItemString(IntPtr pointer, string key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyMapping_HasKey(IntPtr pointer, IntPtr key);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDict_Keys(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDict_Values(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDict_Items(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyDict_Copy(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyDict_Update(IntPtr pointer, IntPtr other);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyDict_Clear(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyDict_Size(IntPtr pointer);


        //====================================================================
        // Python list API
        //====================================================================

        internal static bool PyPyList_Check(IntPtr ob)
        {
            return PyPyObject_TYPE(ob) == Runtime.PyListType;
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyList_New(int size);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyList_AsTuple(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyList_GetItem(IntPtr pointer, int index);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyList_SetItem(IntPtr pointer, int index, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyList_Insert(IntPtr pointer, int index, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyList_Append(IntPtr pointer, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyList_Reverse(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyList_Sort(IntPtr pointer);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyList_GetSlice(IntPtr pointer, int start, int end);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyList_SetSlice(IntPtr pointer, int start, int end, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyList_Size(IntPtr pointer);


        //====================================================================
        // Python tuple API
        //====================================================================

        internal static bool PyPyTuple_Check(IntPtr ob)
        {
            return PyPyObject_TYPE(ob) == Runtime.PyTupleType;
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyTuple_New(int size);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyTuple_GetItem(IntPtr pointer, int index);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyTuple_SetItem(IntPtr pointer, int index, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyTuple_GetSlice(IntPtr pointer, int start, int end);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyTuple_Size(IntPtr pointer);


        //====================================================================
        // Python iterator API
        //====================================================================

#if PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern bool
            PyPyIter_Check(IntPtr pointer);
#elif PYTHON3
        internal static bool
            PyPyIter_Check(IntPtr pointer)
        {
            IntPtr ob_type = (IntPtr)Marshal.PtrToStructure(pointer + ObjectOffset.ob_type, typeof(IntPtr));
            IntPtr tp_iternext = ob_type + TypeOffset.tp_iternext;
            return tp_iternext != null && tp_iternext != _PyPyObject_NextNotImplemented;
        }
#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyIter_Next(IntPtr pointer);

        //====================================================================
        // Python module API
        //====================================================================

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyModule_New(string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPyModule_GetName(IntPtr module);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyModule_GetDict(IntPtr module);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern string
            PyPyModule_GetFilename(IntPtr module);

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyModule_Create2(IntPtr module, int apiver);
#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyImport_Import(IntPtr name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyImport_ImportModule(string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyImport_ReloadModule(IntPtr module);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyImport_AddModule(string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyImport_GetModuleDict();

#if PYTHON3
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPySys_SetArgvEx(
                int argc,
                [MarshalAsAttribute(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] argv,
                int updatepath
            );
#elif PYTHON2
        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPySys_SetArgvEx(
                int argc,
                string[] argv,
                int updatepath
            );
#endif

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPySys_GetObject(string name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPySys_SetObject(string name, IntPtr ob);


        //====================================================================
        // Python type object API
        //====================================================================

        internal static bool PyPyType_Check(IntPtr ob)
        {
            return PyPyObject_TypeCheck(ob, Runtime.PyTypeType);
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyType_Modified(IntPtr type);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern bool
            PyPyType_IsSubtype(IntPtr t1, IntPtr t2);

        internal static bool PyPyObject_TypeCheck(IntPtr ob, IntPtr tp)
        {
            IntPtr t = PyPyObject_TYPE(ob);
            return (t == tp) || PyPyType_IsSubtype(t, tp);
        }

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyType_GenericNew(IntPtr type, IntPtr args, IntPtr kw);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyType_GenericAlloc(IntPtr type, int n);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyType_Ready(IntPtr type);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            _PyPyType_Lookup(IntPtr type, IntPtr name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_GenericGetAttr(IntPtr obj, IntPtr name);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyObject_GenericSetAttr(IntPtr obj, IntPtr name, IntPtr value);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            _PyPyObject_GetDictPtr(IntPtr obj);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyObject_GC_New(IntPtr tp);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyObject_GC_Del(IntPtr tp);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyObject_GC_Track(IntPtr tp);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyObject_GC_UnTrack(IntPtr tp);


        //====================================================================
        // Python memory API
        //====================================================================

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyMem_Malloc(int size);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyMem_Realloc(IntPtr ptr, int size);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyMem_Free(IntPtr ptr);


        //====================================================================
        // Python exception API
        //====================================================================

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_SetString(IntPtr ob, string message);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_SetObject(IntPtr ob, IntPtr message);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyErr_SetFromErrno(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_SetNone(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyErr_ExceptionMatches(IntPtr exception);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyErr_GivenExceptionMatches(IntPtr ob, IntPtr val);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_NormalizeException(IntPtr ob, IntPtr val, IntPtr tb);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern int
            PyPyErr_Occurred();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_Fetch(ref IntPtr ob, ref IntPtr val, ref IntPtr tb);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_Restore(IntPtr ob, IntPtr val, IntPtr tb);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_Clear();

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern void
            PyPyErr_Print();


        //====================================================================
        // Miscellaneous
        //====================================================================

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyMethod_Self(IntPtr ob);

        [DllImport(Runtime.dll, CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal unsafe static extern IntPtr
            PyPyMethod_Function(IntPtr ob);
    }
}
