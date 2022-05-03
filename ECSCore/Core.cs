#define SEPARATE_RENDER_THREAD

#define MULTI_THREAD_SUBSYSTEMS
#undef MULTI_THREAD_SUBSYSTEMS

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

using System.Runtime;
using System.Runtime.InteropServices;

using SFML.Window;
using SFML.Graphics;
using SFML.System;

using ECS.Collections;
using ECS.Animations;
using ECS.Logger;
using ECS.Collision;
using ECS.Graphics;
using ECS.Physics;
using ECS.Strings;
using ECS.UI;
using ECS.Library;
using ECS.Exceptions;
using ECS.Maths;
using ECS.Delegates;
using ECS.GarbageCollection;

namespace ECS
{
    using static CAllocation;
    using static Debug;
    using static UnmanagedCSharp;
    using static Maths.Maths;
    using static ECS.Collections.Generic;
    /// <summary>
    /// Static class used for monitoring the timing of the system.
    /// </summary>
    public static class Time
    {
        private static readonly object TimeSyncRoot = new object();
        /// <summary>
        /// Synchronises threads and updates the current delta time of the engine.
        /// </summary>
        internal struct FrameTimeDelay
        {
            public float DeltaTime0;
            public float DeltaTime1;

            public Thread RenderThread;
            public bool HasRenderUpdatedRecently;

            public Thread GameLoopThread;
            public bool HasGameLoopUpdatedRecently;
        }
        internal static FrameTimeDelay FrameTime = new FrameTimeDelay();
        /// <summary>
        /// Registers that the thread has completed
        /// </summary>
        /// <param name="thread"></param>
        internal static void RegisterThreadContinuance(Thread thread)
        {
            lock(TimeSyncRoot)
            {
                if (thread == FrameTime.RenderThread)
                    FrameTime.HasRenderUpdatedRecently = true;
                else if (thread == FrameTime.GameLoopThread)
                    FrameTime.HasGameLoopUpdatedRecently = true;
            }
        }
        /// <summary>
        /// Indicates the thread should wait while other threads are still running
        /// </summary>
        /// <param name="thread"></param>
        /// <returns></returns>
        internal static bool AreOtherThreadsStillGoing(Thread thread)
        {
            lock(TimeSyncRoot)
            {
                if (thread == FrameTime.GameLoopThread)
                {
                    if (FrameTime.HasRenderUpdatedRecently)
                    {
                        FrameTime.HasRenderUpdatedRecently = false;
                        return true;
                    }
                }
                else if (thread == FrameTime.RenderThread)
                {
                    if (FrameTime.HasGameLoopUpdatedRecently)
                    {
                        FrameTime.HasGameLoopUpdatedRecently = false;
                        return true;
                    }
                }
                return false;
            }
        }
        /// <summary>
        /// Set the render thread of the FrameTime object
        /// </summary>
        /// <param name="thread"></param>
        internal static void SetRenderThread(Thread thread)
        {
            FrameTime.RenderThread = thread;
        }
        /// <summary>
        /// Set the game loop thread of the FrameTime object
        /// </summary>
        /// <param name="thread"></param>
        internal static void SetGameLoopThread(Thread thread)
        {
            FrameTime.GameLoopThread = thread;
        }
        /// <summary>
        /// Returns the previous frames' time to complete, keeps render thread and game loop from colliding
        /// </summary>
        public static float DeltaTime
        {
            get
            {
                return FrameTime.DeltaTime1;
            }
            internal set
            {
                FrameTime.DeltaTime1 = FrameTime.DeltaTime0;
                FrameTime.DeltaTime0 = value;
            }
        }
        /// <summary>
        /// Current Framerate of the running subsystems, excludes rendering times
        /// </summary>
        public static float FPS { get; internal set; } = 0f;
    }
    /// <summary>
    /// Custom class for managing string paths of resources & assets. Used in order to avoid direct use of strings.
    /// </summary>
    public class FilePath
    {
        public string CurrentPath { get; internal set; }
        private FilePath(string path) => CurrentPath = Path.GetFullPath(path);
        public override string ToString() => CurrentPath;

        public static implicit operator string(FilePath path)
        {
            if (path is null)
                throw new NullReferenceException("FilePath.Get() might've returned null as the specified path was not valid as it did not point to an actual file or resource.");
            return path.ToString();
        }
        /// <summary>
        /// Ensures the FilePath is valid by checking if the supplied path is valid, returns null if path is invalid
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static FilePath Get(string path)
        {
            if(File.Exists(Path.GetFullPath(path)))
                return new FilePath(path);
            return null;
        }
    }
    /// <summary>
    /// Used for determining which types are supposed to represent the 'data' in the system. Data tables are created for each 'ICData' type registered.
    /// </summary>
    public interface ICData { }
    /// <summary>
    /// Static class for debugging information. Used primarily in connection to logging data in the console.
    /// </summary>
    public static class Debug
    {
        /// <summary>
        /// Shortcut for Console.WriteLine
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="loggable"></param>
        public static void Log<T>(T loggable) => Console.WriteLine(loggable.ToString());
    }
    /// <summary>
    /// Used to represent individual objects in the engine. These are actual game objects and not an object 'base' class.
    /// </summary>
    public struct CObject : IEquatable<CObject>, IDisposable
    {
        private static long incrementer = 1;
        public static readonly CObject Null = new CObject();
        /// <summary>
        /// Object's index in the object table.
        /// </summary>
        internal long Index;
        /// <summary>
        /// Checks if the object is alive and valid (non-null and not missing)
        /// </summary>
        public bool IsAlive
        {
            get
            {
                if (IsInternalAlive)
                    return true;
                if (IsNull)
                    throw new NullReferenceException();
                throw new MissingReferenceException();
            }
        }
        /// <summary>
        /// Checks if the object is null
        /// </summary>
        public bool IsNull => this.MetaPtr == IntPtr.Zero || this.ID == 0;
        /// <summary>
        /// Internal IsAlive check. Can be checked without throwing an error on failure unlike IsAlive.
        /// </summary>
        internal unsafe bool IsInternalAlive
        {
            get
            {
                if (ObjectTablePtr->GetRef<CObject>((int)Index)->MetaPtr == MetaPtr)
                    return true;
                return false;
            }
        }
        /// <summary>
        /// Pointer to MetaData object associated with this object
        /// </summary>
        internal IntPtr MetaPtr;
        /// <summary>
        /// Converts MetaPtr object to valid MetaData pointer
        /// </summary>
        internal unsafe CMetaData* Meta
        {
            get
            {
                fixed (IntPtr* ptr = &MetaPtr)
                {
                    return (CMetaData*)*ptr;
                }
            }
        }
        /// <summary>
        /// Returns true if the object has data with the specified type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public bool HasDataOf<T>() where T : unmanaged => this.HasDataOf(CLookupTable.GetDataTableIndex<T>());
        /// <summary>
        /// Returns true if the object has data with the specified type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool HasDataOf(Type type) => this.HasDataOf(CLookupTable.GetDataTableIndex(type));
        /// <summary>
        /// Internal data type check. Checks 1 type AND checks if the object is alive.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal unsafe bool HasDataOf(int index)
        {
            if (IsAlive)
                return Meta->HasDataOf(index);
            return false;
        }
        /// <summary>
        /// Internal data type check. Checks multiple types AND checks if the object is alive.
        /// </summary>
        /// <param name="indexes"></param>
        /// <returns></returns>
        internal unsafe bool HasDataOfAll(params int[] indexes)
        {
            if (IsAlive)
                return Meta->HasDataOfAll(indexes);
            return false;
        }
        /// <summary>
        /// Unique ID of this CObject
        /// </summary>
        public long ID { get; private set; }
        /// <summary>
        /// Adds data of T type to the object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        public void AddData<T>(T data) where T : unmanaged
        {
            if (!HasDataOf<T>())
                Objects.AddObjectData(this, data);
        }
        internal unsafe T* GetDataPointer<T>() where T : unmanaged
        {
            var index = CLookupTable.GetDataTableIndex<T>();
            if (HasDataOf(index))
            {
                unsafe
                {
                    return Meta->Get<T>(index);
                }
            }
            throw new DataPointerNullException(typeof(T).Name);
        }
        /// <summary>
        /// Returns copy of T type data associated with the object, throws error if none found
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="DataPointerNullException"></exception>
        public T GetData<T>() where T : unmanaged
        {
            var index = CLookupTable.GetDataTableIndex<T>();
            if (HasDataOf(index))
            {
                unsafe
                {
                    return *Meta->Get<T>(index);
                }
            }
            throw new DataPointerNullException(typeof(T).Name);
        }
        /// <summary>
        /// Allows editing of object data, write function for object data. Edits a single type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action"></param>
        /// <exception cref="DataPointerNullException"></exception>
        public void WriteData<T>(Ref<T> action) where T : unmanaged
        {
            var index = CLookupTable.GetDataTableIndex<T>();
            if (HasDataOf(index))
            {
                unsafe
                {
                    action(ref *Meta->Get<T>(index));
                    return;
                }
            }
            throw new DataPointerNullException(typeof(T).Name);
        }
        /// <summary>
        /// Allows editing of object data, write function for object data. Edits 2 types.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="U"></typeparam>
        /// <param name="action"></param>
        /// <exception cref="DataPointerNullException"></exception>
        public void WriteData<T, U>(RefRef<T, U> action) where T : unmanaged where U : unmanaged
        {
            var index = CLookupTable.GetDataTableIndex<T>();
            var index2 = CLookupTable.GetDataTableIndex<U>();
            if (HasDataOf(index) && HasDataOf(index2))
            {
                unsafe
                {
                    action(ref *Meta->Get<T>(index),
                           ref *Meta->Get<U>(index2));
                    return;
                }
            }
            throw new DataPointerNullException(typeof(T).Name, typeof(U).Name);
        }
        /// <summary>
        /// Completely overwrites T type data to copy of supplied T type data
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <exception cref="DataPointerNullException"></exception>
        public void OverwriteData<T>(T data) where T : unmanaged
        {
            var index = CLookupTable.GetDataTableIndex<T>();
            if (HasDataOf(index))
            {
                unsafe
                {
                    *Meta->Get<T>(index) = data;
                    return;
                }
            }
            throw new DataPointerNullException(typeof(T).Name);
        }
        /// <summary>
        /// Returns the object's hashcode (represented as the object's ID).
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode() => (int)this.ID;
        /// <summary>
        /// Checks if 2 objects are equal. Only uses ID to check this as it should be unique in all situations.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(CObject other) => this.ID == other.ID;
        /// <summary>
        /// Dispose of the object. Calls destroy internally and removes it from the object table.
        /// </summary>
        public void Dispose() => Destroy(this);
        /// <summary>
        /// Dispose of the object's internal data if it's actually alive. Disposes of and frees MetaData. Does not remove it from the object table.
        /// </summary>
        internal void InternalDispose()
        {
            if(IsAlive)
            {
                unsafe
                {
                    Meta->Dispose();
                }
                Free(MetaPtr);
                MetaPtr = IntPtr.Zero;
                ID = 0;
                Index = -1;
            }
        }
        /// <summary>
        /// Destroy the object and set the reference to it to NULL.
        /// </summary>
        /// <param name="obj"></param>
        public static void Destroy(ref CObject obj)
        {
            Destroy(obj);
            obj = Null;
        }
        /// <summary>
        /// Destroys and removes object from the object table, the supplied "obj" will still contain data but return as "missing" if read from/written to
        /// </summary>
        /// <param name="obj"></param>
        public static void Destroy(CObject obj)
        {
            lock (Subsystem.SyncRoot)
            {
                var index = (int)obj.Index;
                obj.InternalDispose();
                Objects.RemoveObjectAtIndex(index);
            }
        }
        /// <summary>
        /// Create a new object with blank metadata and return it. Does not append it to the object table.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        internal static CObject New(long id)
        {
            var obj = new CObject
            {
                ID = id,
                Index = id - 1,
                MetaPtr = CMetaData.New(),
            };
            return obj;
        }
        /// <summary>
        /// Creates a new CObject and appends it to the object table. Use this instead of "new CObject()".
        /// </summary>
        /// <returns></returns>
        public static CObject New() => Objects.AddNewObject(incrementer++);
    }

    /// <summary>
    /// Internal static class for allocating unmanaged data in the engine.
    /// </summary>
    internal static class CAllocation
    {
        /// <summary>
        /// Operates same as C/C++ malloc
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public static IntPtr Malloc(int size)
        {
            return Marshal.AllocHGlobal(size);
        }
        /// <summary>
        /// Operates same as C/C++ free
        /// </summary>
        /// <param name="ptr"></param>
        public static void Free(IntPtr ptr)
        {
            Marshal.FreeHGlobal(ptr);
        }
        /// <summary>
        /// Operates same as C/C++ memset
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="value"></param>
        /// <param name="size"></param>
        public static void MemSet(IntPtr ptr, byte value, int size)
        {
            for (var i = 0; i < size; i++)
            {
                Marshal.WriteByte(ptr + i, value);
            }
        }
        /// <summary>
        /// Operates same as C/C++ memmove
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="source"></param>
        /// <param name="size"></param>
        public static void MemMove(IntPtr destination, IntPtr source, int size)
        {
            for (var i = 0; i < size; i++)
            {
                var _byte = Marshal.ReadByte(source + i);
                Marshal.WriteByte(destination + i, _byte);
            }
        }
    }
    /// <summary>
    /// Core static class for the engine. Manages access to data tables and objects. 'using static UnmanagedCSharp;' is used to explicitly declare the use of this engine's management system.
    /// </summary>
    public static unsafe class UnmanagedCSharp
    {
        /// <summary>
        /// Object Table at 0, Texture table at 1, Collision Tree at 2, Animation Table at 3, returns 4
        /// </summary>
        private const int DataTableStartIndex = 4;
        private static int DefaultDataTableSize = 0;

        /// Table Pointers
        private static IntPtr MainTable = IntPtr.Zero;
        internal static CLookupTable* MainTablePtr => (CLookupTable*)MainTable;
        internal static int DataTableEntries => MainTablePtr->Entries - DataTableStartIndex;
        internal static CHook* ObjectTablePtr => (CHook*)*&MainTablePtr->Value0;
        internal static CHook* TextureTablePtr => (CHook*)*( ( &MainTablePtr->Value0 ) + 1 );
        internal static AABBTree* Tree => (AABBTree*)*( ( &MainTablePtr->Value0 ) + 2 );
        internal static CHook* AnimationTablePtr => (CHook*)*( ( &MainTablePtr->Value0 ) + 3 );
        ///

        /// <summary>
        /// Entry point for setting up table allocation in engine. Not the main entry for the Engine itself but this is called when a new Engine is created.
        /// Object table size and data table size should be equal in most circumstances.
        /// </summary>
        /// <param name="mainSize">Main table size</param>
        /// <param name="objectSize">Size of each object table</param>
        /// <param name="defaultDataSize">Size of each data table</param>
        /// <param name="textureSize">Size of texture table</param>
        /// <param name="animationSize">Size of aimation table</param>
        internal static void Entry(int mainSize = 64, int objectSize = 1024, int defaultDataSize = 1024, int textureSize = 64, int animationSize = 64)
        {
            CLookupTable.CreateTable(mainSize + DataTableStartIndex);
            CLookupTable.AddNewType<CObject>(objectSize);
            CLookupTable.AddNewType<CTextureEntry>(textureSize);
            CLookupTable.CreateCollisionTree(objectSize);
            CLookupTable.AddNewType<CAnimationEntry>(animationSize);
            DefaultDataTableSize = defaultDataSize;
            CLookupTable.AddNewDataType<CTexture>();
            CLookupTable.AddNewDataType<CTransform>();
            CLookupTable.AddNewDataType<CString>();
            CLookupTable.AddNewDataType<CButton>();
            CLookupTable.AddNewDataType<CText>();
            Tree->transformDataTableIndex = CLookupTable.GetDataTableIndex<CTransform>();
        }
        /// <summary>
        /// Custom key-value pair for passing data around.
        /// </summary>
        /// <typeparam name="T1">Item 1's type</typeparam>
        /// <typeparam name="T2">Item 2's type</typeparam>
        public struct CTuple<T1, T2> where T1 : unmanaged where T2 : unmanaged
        {
            public static readonly CTuple<T1, T2> Null = new CTuple<T1,T2>();
            public T1 Item1;
            public T2 Item2;
            public CTuple(T1 item1, T2 item2)
            {
                Item1 = item1;
                Item2 = item2;
            }
        }
        /// <summary>
        /// Main table. Custom lookup table for handling access to other data tables. Only one instance can exist.
        /// </summary>
        internal struct CLookupTable
        {
            private static readonly Dictionary<Type, int> DataTableLookup = new Dictionary<Type, int>();

            public int Entries;
            public int MaxSize;
            public int AllocatedBytes;
            public IntPtr Value0;
            /// <summary>
            /// Add a new entry to the lookup table directly.
            /// </summary>
            /// <param name="ptr"></param>
            /// <exception cref="LookupTableLimitException"></exception>
            internal void Add(IntPtr ptr)
            {
                if (Entries >= MaxSize)
                    throw new LookupTableLimitException(MaxSize);
                fixed (IntPtr* ptrPtr = &Value0)
                {
                    *( ptrPtr + Entries++ ) = ptr;
                }
            }
            /// <summary>
            /// Privately get the index of the data table based on the types' hashcode.
            /// </summary>
            /// <param name="type"></param>
            /// <returns></returns>
            /// <exception cref="DataTableTypeIndexException"></exception>
            private int getDataTable(int type)
            {
                fixed(IntPtr* ptrPtr = &Value0)
                {
                    for (int i = DataTableStartIndex; i < Entries; i++)
                    {
                        var hook = (CHook*)*(ptrPtr + i);
                        if (hook->DataType == type)
                            return i;
                    }
                    throw new DataTableTypeIndexException(type);
                }
            }
            /// <summary>
            /// Add a new type to the main loopup table. Can be either a specialised type or a data type.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="is_data_type"></param>
            /// <param name="size"></param>
            private void addNewType<T>(bool is_data_type, int size = 1024) where T : unmanaged
            {
                CHook.NewTableChain<T>(size);
                if(is_data_type)
                    DataTableLookup.Add(typeof(T), getDataTable(typeof(T).GetHashCode()));
            }
            /// <summary>
            /// Privately return the hook of the data table based on index.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            /// <exception cref="IndexInvalidException"></exception>
            private CHook* getDataTableFromIndex(int index)
            {
                fixed (IntPtr* ptrPtr = &Value0)
                {
                    if (index < 0 || index >= MainTablePtr->MaxSize)
                        throw new IndexInvalidException("Data Type tables", index, 0, MainTablePtr->MaxSize);
                    return (CHook*)*( ptrPtr + index );
                }
            }

            /// <summary>
            /// Registers a new component data type and creates a table for it
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <exception cref="IncompatibleTypeException"></exception>
            public static void AddNewDataType<T>() where T : unmanaged
            {
                if(!typeof(ICData).IsAssignableFrom(typeof(T)))
                {
                    throw new IncompatibleTypeException(typeof(T).Name, typeof(ICData).Name);
                }
                MainTablePtr->addNewType<T>(true, DefaultDataTableSize);
            }

            /// <summary>
            /// Registers a new non-component data type and creates a table for it
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="size"></param>
            public static void AddNewType<T>(int size = 1024) where T : unmanaged => MainTablePtr->addNewType<T>(false, size);

            /// <summary>
            /// Returns the index of the data table associated with the Type object, throws error if not present
            /// </summary>
            /// <param name="type"></param>
            /// <returns></returns>
            /// <exception cref="DictionaryIndexInvalidException"></exception>
            public static int GetDataTableIndex(Type type)
            {
                if (DataTableLookup.TryGetValue(type, out var value))
                    return value;
                throw new DictionaryIndexInvalidException("DataTableLookup", type.Name);
            }
            /// <summary>
            /// Returns the index of the data table associated with the T type generic, throws error if not present
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <returns></returns>
            /// <exception cref="DictionaryIndexInvalidException"></exception>
            public static int GetDataTableIndex<T>()
            {
                if (DataTableLookup.TryGetValue(typeof(T), out var value))
                    return value;
                throw new DictionaryIndexInvalidException("DataTableLookup", typeof(T).Name);
            }
            /// <summary>
            /// Returns the hook of the data table associated with the T type generic
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <returns></returns>
            public static CHook* GetDataTable<T>() where T : unmanaged => MainTablePtr->getDataTableFromIndex(GetDataTableIndex<T>());
            /// <summary>
            /// Returns the hook of the data table based on data table index.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            internal static CHook* GetDataTableFromIndex(int index) => MainTablePtr->getDataTableFromIndex(index);
            /// <summary>
            /// Create, allocate and add the main lookup table. Should only be called one.
            /// </summary>
            /// <param name="size"></param>
            internal static void CreateTable(int size)
            {
                var bytes = ( sizeof(int) * 3 ) + ( sizeof(IntPtr) * size );
                MainTable = Malloc(bytes);
                MemSet(MainTable, 0, bytes);
                var table = (CLookupTable*)MainTable;
                *table = new CLookupTable();
                table->Entries = 0;
                table->MaxSize = size;
                table->AllocatedBytes = bytes;
            }
            /// <summary>
            /// Create, allocate and add the Collision tree. Should only be called once.
            /// </summary>
            /// <param name="size"></param>
            internal static void CreateCollisionTree(int size)
            {
                var bytes = sizeof(AABBTree);
                var entry = Malloc(bytes);
                MemSet(entry, 0, bytes);
                var tree = (AABBTree*)entry;
                *tree = new AABBTree(size);
                MainTablePtr->Add(entry);
            }
        }

        // Custom reference and lookup delegates.
        public delegate bool PredicateIn<T>(in T t);
        public delegate void Ref<T0>(ref T0 data0);
        public delegate void RefObject<T0, O0>(ref T0 data0, ref O0 object0);
        public delegate void RefRef<T0, T1>(ref T0 data0, ref T1 data1);
        public delegate void RefRefObject<T0, T1, O0>(ref T0 data0, ref T1 data1, ref O0 object0);
        public delegate void RefRefRef<T0, T1, T2>(ref T0 data0, ref T1 data1, ref T2 data2);
        public delegate void In<T0>(in T0 data0);
        public delegate bool Compare<T0>(in T0 data);
        //

        /// <summary>
        /// Iterator type for parallel operations. Performs the same action as CHookIterator except splits the workload across multiple threads.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        internal struct CParallelHookIterator<T> where T : unmanaged
        {
            private CHook* internalHook;
            private int totalReturned;
            public int expectedLimit;
            private bool limitReached;
            private Predicate<T> predicateCheck;
            private CLinkableTable* currentTable;
            /// <summary>
            /// Starts and waits for the hook search to complete, performs action when matching object is found
            /// </summary>
            /// <param name="onFindNext">Action to perform per object</param>
            public void Start(Action<T> onFindNext)
            {
                var _this = this;
                while(_this.currentTable != null && !_this.limitReached)
                {
                    Parallel.For(0, internalHook->TableSize, x =>
                    {
                        if (_this.limitReached)
                            return;

                        if (_this.currentTable->TryGetRef(new T(), x, out var next) && _this.predicateCheck(*next))
                        {
                            Interlocked.Increment(ref _this.totalReturned);
                            if (_this.totalReturned >= _this.expectedLimit)
                                _this.limitReached = true;
                            onFindNext(*next);
                        }
                    });
                    _this.currentTable = _this.currentTable->Next;
                }
            }
            /// <summary>
            /// Resets the hook iterator, allows for re-use of hook iterator
            /// </summary>
            public void Reset()
            {
                currentTable = internalHook->Start;
                totalReturned = 0;
                limitReached = expectedLimit > 0 ? false : true;
            }
            /// <summary>
            /// Object iterator shortcut. Create an object iterator for all objects in the object table
            /// </summary>
            /// <param name="expectedLimit"></param>
            /// <param name="check"></param>
            /// <returns></returns>
            public static CParallelHookIterator<CObject> GetIterator(int expectedLimit, Predicate<CObject> check)
            {
                var iterator = new CParallelHookIterator<CObject>();
                iterator.internalHook = ObjectTablePtr;
                iterator.currentTable = iterator.internalHook->Start;
                iterator.totalReturned = 0;
                iterator.expectedLimit = expectedLimit;
                iterator.limitReached = expectedLimit > 0 ? false : true;
                iterator.predicateCheck = check;
                return iterator;
            }
        }
        /// <summary>
        /// Iterator for stepping through data or object tables. Checks each reference and then returns it. Combination of a for-loop and a foreach-loop. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        internal struct CHookIterator<T> where T : unmanaged
        {
            private CHook* internalHook;
            private int currentIndex;
            private int totalReturned;
            public int expectedLimit;
            private bool limitReached;
            private Predicate<T> predicateCheck;
            private CLinkableTable* currentTable;
            /// <summary>
            /// Finds the next object that matches the search criteria
            /// </summary>
            public T* Next
            {
                get
                {
                    T* next = null;
                    while (!limitReached)
                    {
                        if (currentIndex >= internalHook->TableSize)
                        {
                            if (currentTable->Next == null)
                            {
                                limitReached = true;
                                break;
                            }
                            currentIndex = 0;
                            currentTable = currentTable->Next;
                        }
                        if (currentTable->TryGetRef(new T(), currentIndex++, out next) && predicateCheck(*next))
                        {
                            totalReturned++;
                            if (totalReturned >= expectedLimit)
                                limitReached = true;
                            return next;
                        }
                    }
                    return next;
                }
            }
            /// <summary>
            /// Resets the hook iterator, allows for re-use of hook iterator
            /// </summary>
            public void Reset()
            {
                currentTable = internalHook->Start;
                currentIndex = 0;
                totalReturned = 0;
                limitReached = expectedLimit > 0 ? false : true;
            }
            /// <summary>
            /// Object iterator shortcut. Create an object iterator for all objects in the object table
            /// </summary>
            /// <param name="expectedLimit"></param>
            /// <param name="check"></param>
            /// <returns></returns>
            public static CHookIterator<CObject> GetIterator(int expectedLimit, Predicate<CObject> check)
            {
                var iterator = new CHookIterator<CObject>();
                iterator.internalHook = ObjectTablePtr;
                iterator.currentTable = iterator.internalHook->Start;
                iterator.currentIndex = 0;
                iterator.totalReturned = 0;
                iterator.expectedLimit = expectedLimit;
                iterator.limitReached = expectedLimit > 0 ? false : true;
                iterator.predicateCheck = check;
                return iterator;
            }
        }
        /// <summary>
        /// The start point of each object or data table linked list. Provides access to all data tables containing the declared data type.
        /// </summary>
        internal struct CHook
        {
            public int SizeOfData;
            public int DataType;
            public int Count;
            public int TableSize;
            public IntPtr Empty;
            public CLinkableTable* Start;
            /// <summary>
            /// Empty data entry. Used to check if data is valid/exists. Allows for byte-removal of data if no data type is used.
            /// </summary>
            public ReadOnlySpan<byte> EmptySpan
            {
                get
                {
                    fixed (IntPtr* ptr = &Empty)
                    {
                        return new ReadOnlySpan<byte>(( *ptr ).ToPointer(), SizeOfData);
                    }
                }
            }
            public int MaxCapacity => Count * TableSize;
            /// <summary>
            /// Returns the max size of all tables combined attached to this hook
            /// </summary>
            /// <returns></returns>
            public int TotalHookSize()
            {
                int size = 0;
                var table = Start;
                for (int i = 0; i < Count; i++)
                {
                    size += table->MaxSize;
                    table = table->Next;
                }
                return size;
            }
            /// <summary>
            /// Returns the total number of entries of all tables combined attached to this hook
            /// </summary>
            /// <returns></returns>
            public int TotalHookCount()
            {
                int count = 0;
                var table = Start;
                for (int i = 0; i < Count; i++)
                {
                    count += table->Count;
                    table = table->Next;
                }
                return count;
            }
            /// <summary>
            /// Allows for indexing of all tables' entries.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            private CTuple<int, int> GetTableAndEntryIndexes(int index)
            {
                var tableIndex = index / TableSize;
                var entryIndex = index - ( tableIndex * TableSize );
                return new CTuple<int, int>(tableIndex, entryIndex);
            }
            /// <summary>
            /// Allows for indexing of all tables.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="index"></param>
            /// <returns></returns>
            /// <exception cref="LinkedTableNullException"></exception>
            private CLinkableTable* GetTable<T>(int index) where T : unmanaged
            {
                var table = Start;
                for (int i = 0; i < index; i++)
                    table = table->Next;
                if (table == null)
                    throw new LinkedTableNullException(index, Count);
                return table;
            }
            /// <summary>
            /// Find entry by reference (pointer)
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="predicate"></param>
            /// <returns></returns>
            public T* FindRef<T>(PredicateIn<T> predicate) where T : unmanaged
            {
                var table = Start;
                for (int i = 0; i < Count; i++)
                {
                    for (int j = 0; j < TableSize; j++)
                    {
                        if (predicate(in *table->GetRef<T>(j)))
                        {
                            return table->GetRef<T>(j);
                        }
                    }
                    table = table->Next;
                }
                return null;
            }
            /// <summary>
            /// Find entry by instance (copy)
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="predicate"></param>
            /// <returns></returns>
            /// <exception cref="ObjectPointerNullException"></exception>
            public T Find<T>(PredicateIn<T> predicate) where T : unmanaged
            {
                var item = FindRef(predicate);
                if (item == null)
                    throw new ObjectPointerNullException();
                return *item;
            }
            /// <summary>
            /// Get entry by reference (pointer)
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="index"></param>
            /// <returns></returns>
            public T* GetRef<T>(int index) where T : unmanaged
            {
                var tuple = GetTableAndEntryIndexes(index);
                return GetTable<T>(tuple.Item1)->GetRef<T>(tuple.Item2);
            }
            /// <summary>
            /// Get entry by instance (copy)
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="index"></param>
            /// <returns></returns>
            public T Get<T>(int index) where T : unmanaged => *GetRef<T>(index);
            /// <summary>
            /// Add a new entry to the hook and dynamically append it to the next available table.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="item"></param>
            /// <returns></returns>
            /// <exception cref="LinkedTableNullException"></exception>
            /// <exception cref="LinkedTableNotNullException"></exception>
            public int AddNew<T>(T item) where T : unmanaged
            {
                var index = 0;
                var table = Start;
                for (int i = 0; i < Count; i++)
                {
                    if (table->Count >= table->MaxSize)
                    {
                        index += TableSize;
                        if (table->Next == null)
                            break;
                        table = table->Next;
                        continue;
                    }
                    index += table->AddNew(item);
                    return index;
                }

                if (table == null)
                    throw new LinkedTableNullException(index, Count);
                if (table->Next != null)
                    throw new LinkedTableNotNullException(index);

                NewTable<T>(table);
                index += table->Next->AddNew(item);
                return index;
            }
            /// <summary>
            /// Remove an entry from the hook and dynamically remove it from the associated table (type-based)
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="index"></param>
            /// <exception cref="IndexInvalidException"></exception>
            /// <exception cref="LinkedTableNullException"></exception>
            public void RemoveAt<T>(int index) where T : unmanaged
            {
                if (index < 0 || index >= MaxCapacity)
                    throw new IndexInvalidException("a Hooks' linked tables", index, 0, MaxCapacity);
                var tuple = GetTableAndEntryIndexes(index);
                var table = Start;
                for (int i = 0; i < tuple.Item1; i++)
                    table = table->Next;
                if (table == null)
                    throw new LinkedTableNullException(tuple.Item1, Count);
                table->RemoveAt(tuple.Item2, new T());
                if (table->Count == 0 && table->WriteIndex == table->MaxSize)
                {
                    table->WriteIndex = 0;
                }
            }
            /// <summary>
            /// Remove an entry from the hook and dynamically remove it from the associated table (size-based)
            /// </summary>
            /// <param name="index"></param>
            /// <exception cref="IndexInvalidException"></exception>
            /// <exception cref="LinkedTableNullException"></exception>
            public void ByteRemovalAt(int index)
            {
                if (index < 0 || index >= MaxCapacity)
                    throw new IndexInvalidException("a Hooks' linked tables", index, 0, MaxCapacity);
                var tuple = GetTableAndEntryIndexes(index);
                var table = Start;
                for (int i = 0; i < tuple.Item1; i++)
                    table = table->Next;
                if (table == null)
                    throw new LinkedTableNullException(tuple.Item1, Count);
                table->ByteRemovalAt(index, SizeOfData, EmptySpan);
                if (table->Count == 0 && table->WriteIndex == table->MaxSize)
                {
                    table->WriteIndex = 0;
                }
            }
            private void NewTable<T>(CLinkableTable* table) where T : unmanaged
            {
                table->Next = CLinkableTable.NewTable(table, TableSize, new T());
                Count++;
            }
            /// <summary>
            /// Dynamically add a new table onto the end of the last table.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            public void AppendNewTable<T>() where T : unmanaged
            {
                var table = Start;
                while (table->Next != null)
                    table = table->Next;
                NewTable<T>(table);
            }
            /// <summary>
            /// Create a new hook and tables with T type entries
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="size"></param>            
            public static void NewTableChain<T>(int size = 1024) where T : unmanaged
            {
                var outer = Malloc(sizeof(CHook));
                MemSet(outer, 0, sizeof(CHook));
                var hook = (CHook*)outer;
                hook->Empty = Malloc(sizeof(T));
                MemSet(hook->Empty, 0, sizeof(T));
                hook->DataType = typeof(T).GetHashCode();
                hook->SizeOfData = sizeof(T);
                hook->Count = 1;
                hook->TableSize = size;
                hook->Start = CLinkableTable.NewTable(null, size, new T());
                MainTablePtr->Add(outer);
            }
        }
        /// <summary>
        /// A table node in the hook's linked list. Individual table for accessing only its own contents or for returning the next table in the list.
        /// </summary>
        internal struct CLinkableTable
        {
            public int ReadIndex;
            public int WriteIndex;
            public int Count;
            public int MaxSize;
            public int AllocatedBytes;
            public CLinkableTable* Previous;
            public CLinkableTable* Next;
            public byte Value0;
            /// <summary>
            /// Empty the table of all data. Similar to MemSet but sets each entry to empty rather than every byte.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="empty"></param>
            private void ClearTable<T>(T empty) where T : unmanaged
            {
                fixed (byte* ptr = &Value0)
                {
                    for (int i = 0; i < MaxSize; i++)
                    {
                        *( (T*)ptr + i ) = empty;
                    }
                }
            }
            /// <summary>
            /// Add a new entry to this table specifically, not the table's hook.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="item"></param>
            /// <returns></returns>
            /// <exception cref="IndexInvalidException"></exception>
            public int AddNew<T>(T item) where T : unmanaged
            {
                if (WriteIndex >= MaxSize || Count >= MaxSize)
                    throw new IndexInvalidException("a Hooks' linked tables", WriteIndex > Count ? WriteIndex : Count, 0, MaxSize);
                fixed (byte* ptr = &Value0)
                {
                    *( (T*)ptr + WriteIndex++ ) = item;
                    Count++;
                }
                return WriteIndex - 1;
            }
            /// <summary>
            /// Remove an entry from this table specifically, not the table's hook. (type-based)
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="index"></param>
            /// <param name="empty"></param>
            /// <exception cref="IndexInvalidException"></exception>
            public void RemoveAt<T>(int index, T empty) where T : unmanaged
            {
                if (index < 0 || index >= MaxSize)
                    throw new IndexInvalidException("a Hooks' linked tables", index, 0, MaxSize);
                fixed (byte* ptr = &Value0)
                {
                    if (( (T*)ptr + index )->Equals(empty))
                        return;
                    *( (T*)ptr + index ) = empty;
                    Count--;
                }
            }
            /// <summary>
            /// Remove an entry from this table specifically, not the table's hook. (size-based)
            /// </summary>
            /// <param name="index"></param>
            /// <param name="dataSize"></param>
            /// <param name="emptySpan"></param>
            /// <exception cref="IndexInvalidException"></exception>
            public void ByteRemovalAt(int index, int dataSize, ReadOnlySpan<byte> emptySpan)
            {
                if (index < 0 || index >= MaxSize)
                    throw new IndexInvalidException("a Hooks' linked tables", index, 0, MaxSize);
                fixed (byte* ptr = &Value0)
                {
                    var t = new Span<byte>(ptr + ( index * dataSize ), dataSize);
                    if (t.SequenceEqual(emptySpan))
                        return;
                    t.Clear();
                    Count--;
                }
            }
            /// <summary>
            /// Try to get the entry at index, returns true and outs a reference to the entry if it does not equal an empty entry.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="empty"></param>
            /// <param name="index"></param>
            /// <param name="ptr"></param>
            /// <returns></returns>
            public bool TryGetRef<T>(T empty, int index, out T* ptr) where T : unmanaged
            {
                ptr = GetRef<T>(index);
                return !ptr->Equals(empty);
            }
            /// <summary>
            /// Directly get entry at index by reference (pointer). Not guaranteed to be set to any actual data.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="index"></param>
            /// <returns></returns>
            /// <exception cref="IndexInvalidException"></exception>
            public T* GetRef<T>(int index) where T : unmanaged
            {
                if (index < 0 || index >= MaxSize)
                    throw new IndexInvalidException("a Hooks' linked tables", index, 0, MaxSize);
                fixed (byte* ptr = &Value0)
                {
                    return (T*)ptr + index;
                }
            }
            /// <summary>
            /// Directly get entry at index by instance (copy). Not guaranteed to be set to any actual data.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="index"></param>
            /// <returns></returns>
            public T Get<T>(int index) where T : unmanaged
            {
                return *GetRef<T>(index);
            }
            /// <summary>
            /// Create a new table and return its pointer. Used in conjunction with a new hook or appending a new table to an existing hook. Not used on its own.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="previous"></param>
            /// <param name="size"></param>
            /// <param name="empty"></param>
            /// <returns></returns>
            internal static CLinkableTable* NewTable<T>(CLinkableTable* previous, int size, T empty) where T : unmanaged
            {
                var bytes = ( sizeof(int) * 5 ) + sizeof(IntPtr) + ( sizeof(CLinkableTable*) * 2 ) + ( sizeof(T) * size );
                var entry = Malloc(bytes);
                MemSet(entry, 0, bytes);
                var table = (CLinkableTable*)entry;
                table->AllocatedBytes = bytes;
                table->ReadIndex = 0;
                table->WriteIndex = 0;
                table->Count = 0;
                table->MaxSize = size;
                table->Previous = previous;
                table->Next = null;
                table->ClearTable(empty);
                return table;
            }
        }
        /// <summary>
        /// Unmanaged representation of sprite sheet metadata. Each sprite is represented on the sprite sheet using an index and their texture rectangle on the main texture. 
        /// </summary>
        internal struct CSpriteSheetInfo : IDisposable
        {
            /// <summary>
            /// Private type to hold individual sprite data on a sprite sheet.
            /// </summary>
            private struct IntRectIndex
            {
                public int Index;
                public IntRect Rect;
            }
            public int SpriteCount;
            public Vector2u TotalSize;
            public Vector2u IndividualSpriteSize;
            public IntPtr IntRects;
            public CSpriteSheetInfo(Vector2u total, Vector2u individual)
            {
                var sx = (int) (total.X / individual.X);
                var sy = (int) (total.Y / individual.Y);
                var count = sx * sy;
                SpriteCount = count;
                TotalSize = total;
                IndividualSpriteSize = individual;
                IntRects = Malloc(count * sizeof(IntRectIndex));
                MemSet(IntRects, 0, count * sizeof(IntRectIndex));
                fixed (IntPtr* ptr = &IntRects)
                {
                    var int_rect = (IntRectIndex*)*ptr;
                    for(int y = 0; y < sy; y++)
                    {
                        for(int x = 0; x < sx; x++)
                        {
                            var num_index = (int)( ( y * sy ) + x );
                            var index = int_rect + num_index;
                            index->Index = num_index;
                            index->Rect = new IntRect(x * (int)individual.X, y * (int)individual.X, (int)individual.X, (int)individual.Y);

                        }
                    }
                }
            }
            /// <summary>
            /// Get sprite data on sprite sheet using its index.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            /// <exception cref="IndexInvalidException"></exception>
            public IntRect GetTextureRectAtIndex(int index)
            {
                if (index < 0 || index >= SpriteCount)
                    throw new IndexInvalidException("a sprite sheet", index, 0, SpriteCount);
                fixed (IntPtr* ptr = &IntRects)
                {
                    return ( ( (IntRectIndex*)*ptr ) + index )->Rect;
                }
            }
            /// <summary>
            /// Dispose of the texture. Frees the allocated integer rects for each sprite.
            /// </summary>
            public void Dispose()
            {
                Free(IntRects);
            }
        }
        /// <summary>
        /// Entry for the animation lookup table. Multiple objects with the same animation data will reference only a single/the same CAnimationEntry.
        /// </summary>
        internal struct CAnimationEntry : IDisposable
        {
            /// <summary>
            /// Represents a single frame in an animation. Accessible throughout the core as CTexture saves a copy to keep track of the current animation state.
            /// </summary>
            public struct AnimState
            {
                public static readonly AnimState Null = new AnimState();

                public IntPtr Entry;
                public int Index;
                public AnimState(IntPtr ptr, int index)
                {
                    Entry = ptr;
                    Index = index;
                }
            }
            public static readonly CAnimationEntry Null = new CAnimationEntry();

            public int HashCode;
            public uint FrameCount;
            public IntPtr Frames;

            public CAnimationEntry(string name, uint frameCount)
            {
                FrameCount = frameCount;
                Frames = Malloc((int)( sizeof(AnimState) * frameCount ));
                MemSet(Frames, 0, (int)( sizeof(AnimState) * frameCount ));
                HashCode = name.GetHashCode();
            }
            /// <summary>
            /// Set a frame in an animation. Allows for dynamic creation of animations.
            /// </summary>
            /// <param name="index"></param>
            /// <param name="texture"></param>
            /// <exception cref="IndexInvalidException"></exception>
            public void SetFrame(int index, ref CTexture texture)
            {
                if (index < 0 || index >= FrameCount)
                    throw new IndexInvalidException("an animations' frames", index, 0, (int)FrameCount);
                fixed (IntPtr* pFrames = &Frames)
                {
                    var anim = (AnimState*)*pFrames;
                    *( anim + index ) = new AnimState(texture.TexturePtr, index);
                }
            }
            /// <summary>
            /// Set the internal texture of an object to the pointer saved at the animation's index.
            /// </summary>
            /// <param name="index"></param>
            /// <param name="texture"></param>
            /// <param name="animation"></param>
            /// <exception cref="IndexInvalidException"></exception>
            public void SetTexture(int index, ref CTexture texture, ref CAnimation animation)
            {
                if (index < 0 || index >= FrameCount)
                    throw new IndexInvalidException("an animations' frames", index, 0, (int)FrameCount);
                fixed (IntPtr* pFrames = &Frames)
                {
                    var anim = ( (AnimState*)*pFrames ) + index;
                    animation.animState = *anim;
                    texture.TexturePtr = anim->Entry;
                    texture.Index = anim->Index;
                    texture.UpdateRectOnly();
                }
            }
            /// <summary>
            /// Dispose of the animation. Frees the frames' pointer.
            /// </summary>
            public void Dispose()
            {
                Free(Frames);
            }
        }
        /// <summary>
        /// Entry for the texture lookup table. Multiple objects with the same texture data will reference only a single/the same CTextureEntry.
        /// </summary>
        internal struct CTextureEntry : IDisposable
        {
            public static readonly CTextureEntry Null = new CTextureEntry();
            public int HashCode;
            public CSpriteSheetInfo SheetInfo;
            public IntPtr TexturePtr;
            private Vector2u GetSizeDirectly() => SFMLTexture.sfTexture_getSize(TexturePtr);
            public void Dispose()
            {
                Free(TexturePtr);
            }
            /// <summary>
            /// Create a new sprite from a file. Generates blank sprite sheet info.
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            private static CTextureEntry GetSprite(string filename)
            {
                var rect = new IntRect();
                var entry = new CTextureEntry()
                {
                    HashCode = filename.GetHashCode(),
                    TexturePtr = SFMLTexture.sfTexture_createFromFile(filename, ref rect),
                };
                entry.SheetInfo = new CSpriteSheetInfo();
                return entry;
            }
            /// <summary>
            /// Create a new sprite from a file. Generates sprite sheet info as a single sprite.
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            public static CTextureEntry NewSprite(string filename)
            {
                var entry = GetSprite(filename);
                var size = entry.GetSizeDirectly();
                entry.SheetInfo = new CSpriteSheetInfo(size, size);
                return entry;
            }
            /// <summary>
            /// Create a new sprite sheet from a file. Generates sprite sheet info as a whole sprite sheet.
            /// </summary>
            /// <param name="filename"></param>
            /// <param name="spriteSize"></param>
            /// <returns></returns>
            public static CTextureEntry NewSpriteSheet(string filename, Vector2u spriteSize)
            {
                var entry = GetSprite(filename);
                entry.SheetInfo = new CSpriteSheetInfo(entry.GetSizeDirectly(), spriteSize);
                return entry;
            }
        }
        /// <summary>
        /// External access to objects in the system. Gets objects, object data and allows for iteration over many objects.
        /// </summary>
        public struct Objects
        {
            /// <summary>
            /// Add T type data to the object. Inserts the data in a data table and adds its entry index to the object's metadata.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="cObject"></param>
            /// <param name="data"></param>
            internal static void AddObjectData<T>(CObject cObject, T data) where T : unmanaged
            {
                var table = CLookupTable.GetDataTable<T>();
                var index = table->AddNew(data);
                cObject.Meta->Insert(CLookupTable.GetDataTableIndex<T>(), index, (IntPtr)table->GetRef<T>(index));
            }
            /// <summary>
            /// Create a new object and return a copy of it. Each time the ID increments, this should be called.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            internal static CObject AddNewObject(long index)
            {
                var obj = CObject.New(index);
                ObjectTablePtr->AddNew(obj);
                return obj;
            }
            /// <summary>
            /// Removes the object from the object table. Clears its entry but does not call Dispose/Destroy on it.
            /// </summary>
            /// <param name="index"></param>
            internal static void RemoveObjectAtIndex(int index)
            {
                ObjectTablePtr->RemoveAt<CObject>(index);
            }
            /// <summary>
            /// Get an object based on its saved name. Requires CString data.
            /// </summary>
            /// <param name="name"></param>
            /// <returns></returns>
            public static CObject Get(string name)
            {
                var ptr = GetPointer((in CString _string) => _string.Equals(name));
                if (ptr is null)
                    return CObject.Null;
                return *ptr;
            }
            /// <summary>
            /// Get T type data from an object based on its saved name. Requires CString data.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="name"></param>
            /// <returns></returns>
            public static T GetData<T>(string name) where T : unmanaged => Get(name).GetData<T>();
            /// <summary>
            /// Get pointer to an object based on T type data search criteria.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="predicate"></param>
            /// <returns></returns>
            internal static CObject* GetPointer<T>(PredicateIn<T> predicate) where T : unmanaged
            {
                var index = CLookupTable.GetDataTableIndex<T>();
                var tableCount = CLookupTable.GetDataTable<T>()->TotalHookCount();
                var iterator = CHookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                var next = iterator.Next;
                while (next != null)
                {
                    if (predicate(in *next->GetDataPointer<T>()))
                    {
                        return next;
                    }
                    next = iterator.Next;
                }
                return null;
            }
            /// <summary>
            /// Iterates over the object table. Objects with T type data are 'actioned'. Accesses T type data as reference.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="action"></param>
            /// <param name="multi_threaded"></param>
            public static void Iterate<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                var index = CLookupTable.GetDataTableIndex<T>();
                var tableCount = CLookupTable.GetDataTable<T>()->TotalHookCount();

                if(multi_threaded)
                {
                    var iterator = CParallelHookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>());
                    });
                }
                else
                {
                    var iterator = CHookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                    var next = iterator.Next;
                    while (next != null)
                    {
                        action(ref *next->GetDataPointer<T>());
                        next = iterator.Next;
                    }
                }
            }
            /// <summary>
            /// Iterates over the object table. Objects with T AND U type data are 'actioned'. Accesses T AND U type data as reference.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <typeparam name="U"></typeparam>
            /// <param name="action"></param>
            /// <param name="multi_threaded"></param>
            public static void Iterate<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                var index = CLookupTable.GetDataTableIndex<T>();
                var index2 = CLookupTable.GetDataTableIndex<U>();

                var tableCount = CLookupTable.GetDataTableFromIndex(index)->TotalHookCount();
                var tableCount2 = CLookupTable.GetDataTableFromIndex(index2)->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

                if(multi_threaded)
                {
                    var iterator = CParallelHookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>(),
                               ref *x.GetDataPointer<U>());
                    });
                }
                else
                {
                    var iterator = CHookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                    var next = iterator.Next;
                    while (next != null)
                    {
                        action(ref *next->GetDataPointer<T>(),
                               ref *next->GetDataPointer<U>());
                        next = iterator.Next;
                    }
                }
            }
            /// <summary>
            /// Iterates over the object table. Objects with T, U AND V type data are 'actioned'. Accesses T, U AND V type data as reference.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <typeparam name="U"></typeparam>
            /// <typeparam name="V"></typeparam>
            /// <param name="action"></param>
            /// <param name="multi_threaded"></param>
            public static void Iterate<T, U, V>(RefRefRef<T, U, V> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged where V : unmanaged
            {
                var index = CLookupTable.GetDataTableIndex<T>();
                var index2 = CLookupTable.GetDataTableIndex<U>();
                var index3 = CLookupTable.GetDataTableIndex<V>();

                var tableCount = CLookupTable.GetDataTableFromIndex(index)->TotalHookCount();
                var tableCount2 = CLookupTable.GetDataTableFromIndex(index2)->TotalHookCount();
                var tableCount3 = CLookupTable.GetDataTableFromIndex(index3)->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);
                size = Math.Min(size, tableCount3);

                if (multi_threaded)
                {
                    var iterator = CParallelHookIterator<CObject>.GetIterator(size, x => x.HasDataOfAll(index, index2, index3));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>(),
                               ref *x.GetDataPointer<U>(),
                               ref *x.GetDataPointer<V>());
                    });
                }
                else
                {
                    var iterator = CHookIterator<CObject>.GetIterator(size, x => x.HasDataOfAll(index, index2, index3));
                    var next = iterator.Next;
                    while (next != null)
                    {
                        action(ref *next->GetDataPointer<T>(),
                               ref *next->GetDataPointer<U>(),
                               ref *next->GetDataPointer<V>());
                        next = iterator.Next;
                    }
                }
            }
            /// <summary>
            /// Iterates over the object table. Objects with T type data are 'actioned'. Accesses T type data AND the object itself as reference.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="action"></param>
            /// <param name="mutli_threaded"></param>
            public static void IterateWithObject<T>(RefObject<T, CObject> action, bool mutli_threaded = false) where T : unmanaged
            {
                var index = CLookupTable.GetDataTableIndex<T>();
                var tableCount = CLookupTable.GetDataTable<T>()->TotalHookCount();
                if (mutli_threaded)
                {
                    var iterator = CParallelHookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>(),
                               ref x);
                    });
                }
                else
                {
                    var iterator = CHookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                    var next = iterator.Next;
                    while (next != null)
                    {
                        action(ref *next->GetDataPointer<T>(),
                               ref *next);
                        next = iterator.Next;
                    }
                }
            }
            /// <summary>
            /// Iterates over the object table. Objects with T AND U type data are 'actioned'. Accesses T AND U type data AND the object itself as reference.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <typeparam name="U"></typeparam>
            /// <param name="action"></param>
            /// <param name="mutli_threaded"></param>
            public static void IterateWithObject<T, U>(RefRefObject<T, U, CObject> action, bool mutli_threaded = false) where T : unmanaged where U : unmanaged
            {
                var index = CLookupTable.GetDataTableIndex<T>();
                var index2 = CLookupTable.GetDataTableIndex<U>();

                var tableCount = CLookupTable.GetDataTable<T>()->TotalHookCount();
                var tableCount2 = CLookupTable.GetDataTable<U>()->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

                if(mutli_threaded)
                {
                    var iterator = CParallelHookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>(),
                               ref *x.GetDataPointer<U>(),
                               ref x);
                    });
                }
                else
                {
                    var iterator = CHookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                    var next = iterator.Next;
                    while (next != null)
                    {
                        action(ref *next->GetDataPointer<T>(),
                               ref *next->GetDataPointer<U>(),
                               ref *next);
                        next = iterator.Next;
                    }
                }
            }
            /// <summary>
            /// Special type for caching collision-related tables as all collision queries start out as the same function call.
            /// </summary>
            private struct CollisionCache
            {
                public bool IsSet;
                public int TransformIndex;
                public CHook* TransformHook;
                public int ColliderIndex;
                public CHook* ColliderHook;
                public Predicate<CObject> UniversalCheck;
            }
            private static CollisionCache CCache = new CollisionCache();
            /// <summary>
            /// Update the collision tree publically. Should run once during every collision subsystem update.
            /// </summary>
            public static void UpdateCollisionTree()
            {

                if(!CCache.IsSet)
                {
                    CCache.IsSet = true;
                    CCache.TransformIndex = CLookupTable.GetDataTableIndex<CTransform>();
                    CCache.TransformHook = CLookupTable.GetDataTableFromIndex(CCache.TransformIndex);
                    CCache.ColliderIndex = CLookupTable.GetDataTableIndex<CCollider>();
                    CCache.ColliderHook = CLookupTable.GetDataTableFromIndex(CCache.ColliderIndex);
                    CCache.UniversalCheck = x => x.HasDataOf(CCache.TransformIndex) && x.HasDataOf(CCache.ColliderIndex);
                }

                var tableCount = 0;
                var tableCount2 = 0;
                fixed (CHook** hookPtr = &CCache.TransformHook)
                {
                     tableCount = (*hookPtr)->TotalHookCount();
                }
                fixed(CHook** hookPtr = &CCache.ColliderHook)
                {
                    tableCount2 = ( *hookPtr )->TotalHookCount();
                }
                var size = Math.Min(tableCount, tableCount2);
                var iterator = CHookIterator<CObject>.GetIterator(size, CCache.UniversalCheck);
                var next = iterator.Next;
                while (next != null)
                {
                    var collider = next->GetDataPointer<CCollider>();
                    if (!collider->IsInTree)
                    {
                        Tree->InsertObject(next);
                        collider->IsInTree = true;
                    }
                    else
                    {
                        Tree->UpdateObject(next);
                    }
                    next = iterator.Next;
                }
            }
        }
        /// <summary>
        /// Internal static functions for adding and finding texture entries in the texture lookup table
        /// </summary>
        internal struct Textures
        {
            /// <summary>
            /// Try to find the specified texture. If this fails, attempt to load the texture from the file first then return it.
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            public static IntPtr TryAddTexture(string filename)
            {
                var ptr = FindTexture(filename);
                if(ptr == IntPtr.Zero)
                    ptr = NewTexture(filename);
                return ptr;
            }
            /// <summary>
            /// Find texture by filename. Returns IntPtr.Zero on failure.
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            public static IntPtr FindTexture(string filename) => FindTexture(filename.GetHashCode());
            /// <summary>
            /// Find texture based on unique hashcode generated from the filename.
            /// </summary>
            /// <param name="hashCode"></param>
            /// <returns></returns>
            private static IntPtr FindTexture(int hashCode)
            {
                var entry = TextureTablePtr->FindRef((in CTextureEntry x) => x.HashCode == hashCode);
                if (entry == null)
                    return IntPtr.Zero;
                return (IntPtr)entry;
            }
            /// <summary>
            /// Add a new texture entry to the texture table. As a single sprite.
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            public static IntPtr NewTexture(string filename)
            {
                var index = TextureTablePtr->AddNew(CTextureEntry.NewSprite(filename));
                return (IntPtr)TextureTablePtr->GetRef<CTextureEntry>(index);
            }
            /// <summary>
            /// Add a new texture entry to the texture table. As a sprite sheet.
            /// </summary>
            /// <param name="filename"></param>
            /// <param name="individualSpriteSize"></param>
            /// <returns></returns>
            public static IntPtr NewSpriteSheet(string filename, Vector2u individualSpriteSize)
            {
                var index = TextureTablePtr->AddNew(CTextureEntry.NewSpriteSheet(filename, individualSpriteSize));
                return (IntPtr)TextureTablePtr->GetRef<CTextureEntry>(index);
            }
        }
        /// <summary>
        /// Internal static functions for adding and finding animation entries in the animation lookup table
        /// </summary>
        internal struct Animations
        {
            /// <summary>
            /// Try to find the specified animation. If this fails, attempt to load the animation from the file first then return it.
            /// </summary>
            /// <param name="filename"></param>
            /// <param name="frame_count"></param>
            /// <returns></returns>
            public static IntPtr TryAddAnimation(string filename, uint frame_count)
            {
                var ptr = FindAnimation(filename, frame_count);
                if (ptr == IntPtr.Zero)
                    ptr = NewAnimation(filename, frame_count);
                return ptr;
            }
            /// <summary>
            /// Find animation by filename and the number of frames in the animation. Returns IntPtr.Zero on failure.
            /// </summary>
            /// <param name="filename"></param>
            /// <param name="frame_count"></param>
            /// <returns></returns>
            public static IntPtr FindAnimation(string filename, uint frame_count) => FindAnimation(filename.GetHashCode(), frame_count);
            /// <summary>
            /// Find animation based on unique hashcode generated from the filename and the number of frames in the animation.
            /// </summary>
            /// <param name="hashCode"></param>
            /// <param name="frame_count"></param>
            /// <returns></returns>
            private static IntPtr FindAnimation(int hashCode, uint frame_count)
            {
                var entry = AnimationTablePtr->FindRef((in CAnimationEntry x) => x.HashCode == hashCode && x.FrameCount == frame_count);
                if (entry == null)
                    return IntPtr.Zero;
                return (IntPtr)entry;
            }
            /// <summary>
            /// Adds a new animation entry to the animation table. All frames are blank and must be set one by one before use.
            /// </summary>
            /// <param name="filename"></param>
            /// <param name="frame_count"></param>
            /// <returns></returns>
            public static IntPtr NewAnimation(string filename, uint frame_count)
            {
                var index = AnimationTablePtr->AddNew(new CAnimationEntry(filename, frame_count));
                return (IntPtr)AnimationTablePtr->GetRef<CAnimationEntry>(index);
            }
        }
        /// <summary>
        /// Provides the basis for data access for individual objects. Each object contains metadata about what types it has access to. Creates a separation between objects and their data.
        /// </summary>
        internal struct CMetaData : IDisposable
        {
            public int MaxSize;
            public CTuple<int, IntPtr> DataPtr;
            /// <summary>
            /// Get T type data as reference. Does not guarantee the data is of T type. 
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="tableIndex">Must match the DataTableIndex of T type</param>
            /// <returns></returns>
            public T* Get<T>(int tableIndex) where T : unmanaged
            {
                if (tableIndex < 0 || tableIndex >= MaxSize)
                    return null;
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    return (T*)( ptrPtr + tableIndex )->Item2;
                }
            }
            /// <summary>
            /// Insert a pointer to an existing data entry.
            /// </summary>
            /// <param name="tableIndex">DataTableIndex of T type</param>
            /// <param name="index">Index of data entry in table</param>
            /// <param name="ptr"></param>
            public void Insert(int tableIndex, int index, IntPtr ptr)
            {
                if (tableIndex < 0 || tableIndex >= MaxSize)
                    return;
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    *( ptrPtr + tableIndex ) = new CTuple<int, IntPtr>(index, ptr);
                }
            }
            /// <summary>
            /// Checks if a data entry at tableIndex exists.
            /// </summary>
            /// <param name="tableIndex"></param>
            /// <returns></returns>
            public bool HasDataOf(int tableIndex)
            {
                if (tableIndex < 0 || tableIndex >= MaxSize)
                    return false;
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    return ( ptrPtr + tableIndex )->Item2 != IntPtr.Zero;
                }
            }
            /// <summary>
            /// Checks if data entries of different types all exist.
            /// </summary>
            /// <param name="tableIndexes"></param>
            /// <returns></returns>
            public bool HasDataOfAll(int[] tableIndexes)
            {
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    for (int i = 0; i < tableIndexes.Length; i++)
                    {
                        var index = tableIndexes[i];
                        if (index < 0 || index >= MaxSize)
                            return false;
                        if (( ptrPtr + index )->Item2 == IntPtr.Zero)
                            return false;
                    }
                    return true;
                }
            }
            /// <summary>
            /// Create a new blank MetaData object. This is not saved in a table. Only to the CObject that created it.
            /// </summary>
            /// <returns></returns>
            public static IntPtr New()
            {
                var size = MainTablePtr->Entries + 1;
                var bytes = sizeof(int) + ( sizeof(CTuple<int, IntPtr>) * size );
                var ptr = Malloc(bytes);
                MemSet(ptr, 0, bytes);
                var metaSlot = new CMetaData()
                {
                    MaxSize = size
                };
                *(CMetaData*)ptr = metaSlot;
                return ptr;
            }
            /// <summary>
            /// Destroys the MetaData object and removes each data entry from their respective tables. As the type of each data entry is unknown, the data is removed by data size (clearing bytes manually).
            /// </summary>
            public void Dispose()
            {
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    for(int i = DataTableStartIndex; i < this.MaxSize - 1; i++)
                    {
                        var tuple = *( ptrPtr + i );
                        if(tuple.Item2 != IntPtr.Zero)
                        {
                            var table = CLookupTable.GetDataTableFromIndex(i);
                            table->ByteRemovalAt(tuple.Item1);
                        }
                    }
                }
            }
        }
    }
}
namespace ECS.Library
{
    using static UnmanagedCSharp;
    using static Keyboard;
    /// <summary>
    /// Static class for managing window input. Primarily for getting and setting keystrokes and mouse presses.
    /// </summary>
    public static class Input
    {
        private static readonly Current CurrentInput = new Current();
        internal static bool GetInputBool<Args>(this Args args, Func<Args, bool> predicate) where Args : EventArgs => args != null && predicate(args);
        internal static void SetMouseButtonPressedArgs(MouseButtonEventArgs e) => CurrentInput.MouseButtonPressed = e;
        internal static void SetMouseButtonReleasedArgs(MouseButtonEventArgs e) => CurrentInput.MouseButtonPressed = null;
        internal static void SetKeyPressedArgs(KeyEventArgs e) => CurrentInput.KeyPressed.Add(e);
        internal static void SetKeyReleasedArgs(KeyEventArgs e) => CurrentInput.KeyPressed.RemoveWhere(x => x.Code == e.Code);
        public static bool GetMouseButtonPressed(Mouse.Button mouseButton) => CurrentInput.MouseButtonPressed.GetInputBool((x) => x.Button == mouseButton);
        public static bool GetKeyPressed(Key key) => CurrentInput.KeyPressed.Any(x => x.Code == key);
        public static Vector2f GetMousePosition() => (Vector2f)Mouse.GetPosition(Engine.MainWindow);
        /// <summary>
        /// Provides the wide range of available input methods. Not all are currently supported but they are here for later use.
        /// </summary>
        private class Current
        {
            public JoystickButtonEventArgs JoystickButtonPressed = null;
            public TouchEventArgs TouchMoved = null;
            public TouchEventArgs TouchBegan = null;
            public JoystickConnectEventArgs JoystickDisconnected = null;
            public SizeEventArgs Resized = null;
            public JoystickConnectEventArgs JoystickConnected = null;
            public JoystickButtonEventArgs JoystickButtonReleased = null;
            public TextEventArgs TextEntered = null;
            public KeyEventArgs KeyReleased = null;
            public MouseWheelScrollEventArgs MouseWheelScrolled = null;
            public MouseButtonEventArgs MouseButtonPressed = null;
            public JoystickMoveEventArgs JoystickMoved = null;
            public MouseMoveEventArgs MouseMoved = null;
            public Multi<KeyEventArgs> KeyPressed = new Multi<KeyEventArgs>();
            public MouseButtonEventArgs MouseButtonReleased = null;
            public SensorEventArgs SensorChanged = null;
            public TouchEventArgs TouchEnded = null;
        }
        /// <summary>
        /// Special type for when some input methods have simultaneous input such as keystrokes.
        /// </summary>
        /// <typeparam name="Args"></typeparam>
        private class Multi<Args> where Args : EventArgs
        {
            private readonly List<Args> argsList = new List<Args>();

            public void Add(Args args) => argsList.Add(args);
            public bool Remove(Args args) => argsList.Remove(args);
            public int RemoveWhere(Predicate<Args> predicate) => argsList.RemoveAll(predicate);
            public void Clear() => argsList.Clear();
            public bool Any(Func<Args, bool> predicate)
            {
                try
                {
                    return argsList.Any(predicate);
                }
                catch(InvalidOperationException)
                {
                    return false;
                }
            }
        }
    }
    /// <summary>
    /// Abstract, base of the Engine. This type is inherited by the user to customise their engine implementation.
    /// </summary>
    public abstract class Engine
    {
        /// <summary>
        /// Provides information about the current window. This is drawn to during texture draw calls.
        /// </summary>
        public class EngineWindow : RenderWindow
        {
            public static Vector2u WindowDimensions => MainEngine.Settings.WindowDimensions;
            public static AABB GetWindowBoundingBox => new AABB(0, 0, MainEngine.Settings.WindowDimensions.X, MainEngine.Settings.WindowDimensions.Y);
            public EngineWindow(VideoMode mode, string name, uint frame_rate) : base(mode, name)
            {
                SetFramerateLimit(frame_rate);
                this.Closed += OnQuit;
                this.KeyPressed += OnKeyPress;
                this.KeyReleased += OnKeyRelease;
                this.MouseButtonPressed += OnMousePress;
                this.MouseButtonReleased += OnMouseRelease;
            }
            /// <summary>
            /// Closes the window and writes debug logs if available.
            /// </summary>
            public override void Close()
            {
                base.Close();
#if DEBUG
                Logger.Logger.Instance.WriteAllLogs();
#endif
            }

            /// Input methods
            private void OnQuit(object sender, EventArgs args) => this.Close();
            private void OnKeyPress(object sender, KeyEventArgs args) => Input.SetKeyPressedArgs(args);
            private void OnKeyRelease(object sender, KeyEventArgs args) => Input.SetKeyReleasedArgs(args);
            private void OnMousePress(object sender, MouseButtonEventArgs args) => Input.SetMouseButtonPressedArgs(args);
            private void OnMouseRelease(object sender, MouseButtonEventArgs args) => Input.SetMouseButtonReleasedArgs(args);
            ///
        }

        internal static Engine MainEngine = null;
        internal static EngineWindow MainWindow => MainEngine?.ThisWindow;

#if DEBUG
        internal static Process EngineProcess = null;
#endif

        internal EngineWindow ThisWindow = null;

        private readonly List<string> setEngineFlags = new List<string>();
        /// <summary>
        /// User-defined setup subroutine. Typically for allocating resources and creating main menus.
        /// </summary>
        public abstract void Initialise();
        /// <summary>
        /// User-defined engine settings. These control the main table allocation sizes and window dimensions. Allows for quick setting of specialised subsystems.
        /// </summary>
        public virtual EngineSettings Settings => new EngineSettings(4, 1024, 1024, 10, new Vector2u(800, 600), "Window", false, false, false);
        /// <summary>
        /// Main engine loop. Updates all subsystems. This is called while the render window is open.
        /// </summary>
        private void GameLoop()
        {
            var delta = Time.DeltaTime;
            void updateSubsystem(Subsystem system)
            {
                if (system == null)
                    return;

                if(system.IsStarted && system.IsEnabled)
                {
#if DEBUG
                    var time = DateTime.Now;
                    system.Update(delta);
                    var now = DateTime.Now;
                    Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, system.Name, ( now - time ).TotalMilliseconds.ToString());
#else
                    system.Update(delta);
#endif
                }
            }

            for(var i = 0; i < Collection.ValidSubIndexes.Count; i++)
            {
                var subsystems = Collection.SortedSubsystems[Collection.ValidSubIndexes[i]];
                for(var j = 0; j < subsystems.Count; j++)
                {
#if MULTI_THREAD_SUBSYSTEMS
                    if (subsystems[j].Parallelable)
                    {
                        Parallel.For(j, subsystems.Count, x =>
                        {
                            updateSubsystem(subsystems[x]);
                        });
                        break;
                    }
#endif
                    updateSubsystem(subsystems[j]);
                }
            }

#if SEPARATE_RENDER_THREAD
            Time.RegisterThreadContinuance(Thread.CurrentThread);
            while (!Time.AreOtherThreadsStillGoing(Thread.CurrentThread))
                ;
#endif
        }

        /// <summary>
        /// Start all enabled subsystems. Sorts all subsystems afterwards and organises them based on priority.
        /// </summary>
        protected void StartSubsystems()
        {
            foreach (var item in Collection.Subsystems)
                if (!item.IsStarted && item.IsEnabled)
                    item.Startup();
            Collection.SortSubsystems();
        }
        /// <summary>
        /// Stop the currently running engine. Closes the render window.
        /// </summary>
        public static void Stop()
        {
            if (MainEngine != null)
            {
                MainWindow.Close();
            }
        }
        /// <summary>
        /// Get whether a commandline flag was set. Requires the engine to exist.
        /// </summary>
        /// <param name="flag"></param>
        /// <returns></returns>
        public static bool IsEngineFlagSet(string flag) => MainEngine.setEngineFlags.Contains(flag);
        /// <summary>
        /// Start a user-defined engine. The engineType must inherit from the base engine. Also passes any available engine flags and sets them as true.
        /// </summary>
        /// <param name="engineType"></param>
        /// <param name="engineFlags"></param>
        public static void Start(Type engineType, params string[] engineFlags)
        {
            if (engineType.IsSubclassOf(typeof(Engine)))
            {
#if SEPARATE_RENDER_THREAD
                Time.SetGameLoopThread(Thread.CurrentThread);
#endif
#if DEBUG
                Logger.Logger.CreateLogger("TempLog_" + DateTime.Now.Ticks.ToString());
                EngineProcess = Process.GetCurrentProcess();
#endif

                MainEngine = (Engine)Activator.CreateInstance(engineType);

                MainEngine.setEngineFlags.AddRange(engineFlags);

                Entry(MainEngine.Settings.MainTableSize, MainEngine.Settings.ObjectTableSize, MainEngine.Settings.DataTableSize, MainEngine.Settings.TextureTableSize);

                Collection.AddNewSubsystem<RenderSubsystem>(0, false);
                Collection.AddNewSubsystem<UISubsystem>(1, false);

                for(var i = 0; i < 2; i++) // special case exception
                {
                    Collection.Subsystems[i].Startup();
                    Collection.ValidSubIndexes.Add(i);
                    Collection.SortedSubsystems.Add(i, new List<Subsystem> { Collection.Subsystems[i] });
                }

                if (MainEngine.Settings.EnablePhysics)
                {
                    Subsystem.AddNewDataType<CPhysicsBody>();
                    Collection.AddNewSubsystem<PhysicsSubsystem>(10, false);
                }
                if (MainEngine.Settings.EnableCollisions)
                    Subsystem.AddNewDataType<CCollider>();
                if(MainEngine.Settings.EnableAnimations)
                    Subsystem.AddNewDataType<CAnimation>();

#if DEBUG
                var initTime = DateTime.Now;
                MainEngine.Initialise();
                var initNow = DateTime.Now;
                Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, "Initialisation", ((float)( initNow - initTime ).TotalMilliseconds).ToString());
#else
                MainEngine.Initialise();
#endif
                Collection.SubsystemsToTestFor(new KeyValuePair<Type, bool>(typeof(CollisionSubsystem), MainEngine.Settings.EnableCollisions),
                                               new KeyValuePair<Type, bool>(typeof(AnimationSubsystem), MainEngine.Settings.EnableAnimations));

                Subsystem.SystemFlags.SetFlags();


                while (MainEngine.ThisWindow is null && Time.FrameTime.DeltaTime0 == 0f)
                    ;

                while (MainWindow.IsOpen)
                {
                    var gltime = DateTime.Now;
                    MainEngine.GameLoop();
                    var glnow = DateTime.Now;
                    float mpf = (float)( glnow - gltime ).TotalMilliseconds;
                    Time.FPS = 1000f / mpf;
#if DEBUG
                    Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, "GameLoop", mpf.ToString());
                    Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, "RAM Usage (Bytes)", EngineProcess.WorkingSet64.ToString());
#endif
                }
            }
        }
    }
    /// <summary>
    /// Provides customisable information for the engine implementation.
    /// </summary>
    public struct EngineSettings
    {
        public int MainTableSize;
        public int ObjectTableSize;
        public int DataTableSize;
        public int TextureTableSize;
        public Vector2u WindowDimensions;
        public string WindowName;
        public bool EnablePhysics;
        public bool EnableCollisions;
        public bool EnableAnimations;
        public uint DesiredFrameRate;
        public EngineSettings(int mSize, int otSize, int dtSize, int texSize, Vector2u window_dimensions, string name, bool enable_physics, bool enable_collisions, bool enable_animations, uint desiredFrameRate = 60)
        {
            MainTableSize = mSize;
            ObjectTableSize = otSize;
            DataTableSize = dtSize;
            TextureTableSize = texSize;
            WindowDimensions = window_dimensions;
            WindowName = name;
            EnablePhysics = enable_physics;
            EnableCollisions = enable_collisions;
            EnableAnimations = enable_animations;
            DesiredFrameRate = desiredFrameRate;
        }
    }
    /// <summary>
    /// Static class for adding subsystems to the current collection. Provides sorting and testing methods for subsystems.
    /// </summary>
    internal static class Collection
    {
        internal static readonly List<Subsystem> Subsystems = new List<Subsystem>();
        /// <summary>
        /// Dynamically create a new subsystem of System type. Sets the priority of the subsystem and whether it can run in parallel to others.
        /// </summary>
        /// <typeparam name="System"></typeparam>
        /// <param name="priority"></param>
        /// <param name="parallel"></param>
        internal static void AddNewSubsystem<System>(int priority = 5, bool parallel = false) where System : Subsystem
        {
            priority = Maths.Maths.Clamp(priority, 0, 10);
            var type = typeof(System);
            if (!type.IsAbstract && !Subsystems.Any(x => x.GetType() == type))
            {
                var subsystem = (Subsystem)Activator.CreateInstance(type);
                subsystem.Priority = priority;
                subsystem.Parallelable = parallel;
                Subsystems.Add(subsystem);
            }
        }
        /// <summary>
        /// Checks if required subsystems are implemented or not. These are determined by the user-defined engine settings.
        /// </summary>
        /// <param name="subsystemTypes"></param>
        /// <exception cref="SubsystemNotImplementedException"></exception>
        /// <exception cref="SubsystemImplementedException"></exception>
        internal static void SubsystemsToTestFor(params KeyValuePair<Type, bool>[] subsystemTypes)
        {
            foreach(var kvp in subsystemTypes)
            {
                var type = kvp.Key;
                bool foundSubsystem = Subsystems.Any(x => x.GetType().IsSubclassOf(type));

                if (kvp.Value && !foundSubsystem)
                    throw new SubsystemNotImplementedException(type.Name);
                if (!kvp.Value && foundSubsystem)
                    throw new SubsystemImplementedException(type.Name);
            }
        }
        internal static List<int> ValidSubIndexes = new List<int>();
        internal static readonly Dictionary<int, List<Subsystem>> SortedSubsystems = new Dictionary<int, List<Subsystem>>();
        /// <summary>
        /// Internal sort method for subsystems. Organises them by priority.
        /// </summary>
        internal static void SortSubsystems()
        {
            ValidSubIndexes.Clear();
            SortedSubsystems.Clear();

            for(var i = 0; i < Subsystems.Count; i++)
            {
                var subsystem = Subsystems[i];
                if(SortedSubsystems.TryGetValue(subsystem.Priority, out var subsystems))
                    subsystems.Add(subsystem);
                else
                    SortedSubsystems.Add(subsystem.Priority, new List<Subsystem> { subsystem });

                if (!ValidSubIndexes.Contains(subsystem.Priority))
                    ValidSubIndexes.Add(subsystem.Priority);
            }

            ValidSubIndexes = ValidSubIndexes.OrderBy(x => x).ToList();

            for(var i = 0; i < ValidSubIndexes.Count; i++)
            {
                var index = ValidSubIndexes[i];
                SortedSubsystems[index] = SortedSubsystems[index].OrderBy(x => x.Parallelable).ToList();
            }
#if DEBUG
            for (var i = 0; i < ValidSubIndexes.Count; i++)
            {
                var index = ValidSubIndexes[i];
                Debug.Log(index);
                foreach (var sub in SortedSubsystems[index])
                {
                    Debug.Log(sub.Name + " : " + sub.Parallelable);
                }
            }
#endif
        }
    }
    /// <summary>
    /// Abstract, base class for subsystems. This type is inherited by the user to create custom subsystem implementations.
    /// </summary>
    public abstract class Subsystem
    {
        internal static readonly object SyncRoot = new object();
        /// <summary>
        /// Internal global flags to stop the user adding new data types or subsystems after engine initialisation.
        /// </summary>
        internal struct SubsystemFlags
        {
            public bool AddNewDataTypes;
            public bool AddNewSubsystems;
            public SubsystemFlags(bool add_data, bool add_systems)
            {
                AddNewDataTypes = add_data;
                AddNewSubsystems = add_systems;
            }
            public void SetFlags()
            {
                AddNewDataTypes = false;
                AddNewSubsystems = false;
            }
        }
        internal static SubsystemFlags SystemFlags = new SubsystemFlags(true, true);
        /// <summary>
        /// Add a new data type and create a new data table for it. Throws an error if this is called outside of Engine.Initialise().
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="DisallowedSubsystemAddFlagException"></exception>
        public static void AddNewDataType<T>() where T : unmanaged
        {
            if (SystemFlags.AddNewDataTypes)
                CLookupTable.AddNewDataType<T>();
            else
                throw new DisallowedSubsystemAddFlagException(typeof(T).Name);
        }
        /// <summary>
        /// Add a new subsystem. Throws an error if this is called outside of Engine.Initialise().
        /// </summary>
        /// <typeparam name="System"></typeparam>
        /// <param name="priority"></param>
        /// <param name="parallel"></param>
        /// <exception cref="DisallowedSubsystemAddFlagException"></exception>
        public static void AddNewSubsystem<System>(int priority = 5, bool parallel = false) where System : Subsystem
        {
            if (SystemFlags.AddNewSubsystems)
                Collection.AddNewSubsystem<System>(priority, parallel);
            else
                throw new DisallowedSubsystemAddFlagException(typeof(System).Name);
        }
        /// <summary>
        /// Should not be called directly.
        /// </summary>
        public Subsystem()
        {
            Name = GetType().Name;
        }
        public readonly string Name;
        public bool IsEnabled { get; set; } = true;
        public bool IsStarted { get; private set; } = false;
        public int Priority { get; internal set; }
        public bool Parallelable { get; internal set; }
        /// <summary>
        /// Updates the subsystem with Time.DeltaTime. Called per frame if the subsystem is started and enabled.
        /// </summary>
        /// <param name="deltaSeconds"></param>
        public abstract void Update(float deltaSeconds);
        public virtual void Startup() { IsStarted = true; }
    }
    /// <summary>
    /// Special case subsystem used for rendering textures to the current RenderWindow. Locks the subsystem sync root when drawing to prevent invalid draws.
    /// </summary>
    public sealed class RenderSubsystem : Subsystem
    {
#if SEPARATE_RENDER_THREAD
        internal static Thread RenderThread { get; private set; } = null;
#endif
        public override void Startup()
        {
            base.Startup();
#if SEPARATE_RENDER_THREAD
            if (RenderThread != null)
                throw new RenderThreadAlreadyExistsException();
            IsEnabled = false;
            RenderThread = new Thread(() => specialCaseUpdate());
            RenderThread.Start();
#else
            Engine.MainEngine.ThisWindow = new Engine.EngineWindow(new VideoMode(Engine.MainEngine.Settings.WindowDimensions), Engine.MainEngine.Settings.WindowName, Engine.MainEngine.Settings.DesiredFrameRate);
#endif
        }
        public override void Update(float deltaSeconds)
        {
#if !SEPARATE_RENDER_THREAD
            Engine.MainWindow.DispatchEvents();
            var time = DateTime.Now;
            Objects.Iterate((ref Texture texture, ref Transform transform) =>
            {
                var states = new RenderStates(transform.SFMLTransform);
                texture.Draw(Engine.MainWindow, states);
            });

            Objects.Iterate((ref UI.Text text, ref Transform transform) =>
            {
                var states = new RenderStates(transform.SFMLTransform);
                text.Draw(Engine.MainWindow, states);
            });
            Engine.MainWindow.Display();
            Engine.MainWindow.Clear();
            CTime.DeltaTime = (float)( ( DateTime.Now - time ).TotalSeconds );
#if DEBUG
            Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, "Render", ( CTime.DeltaTime * 1000 ).ToString());
#endif
#endif
        }
#if SEPARATE_RENDER_THREAD
        internal void specialCaseUpdate()
        {

            Engine.MainEngine.ThisWindow = new Engine.EngineWindow(new VideoMode(Engine.MainEngine.Settings.WindowDimensions), Engine.MainEngine.Settings.WindowName, Engine.MainEngine.Settings.DesiredFrameRate);

            Time.SetRenderThread(Thread.CurrentThread);

            while (Engine.MainWindow.IsOpen)
            {
                Engine.MainWindow.DispatchEvents();
                var time = DateTime.Now;
                lock (SyncRoot)
                {
                    Objects.Iterate((ref CTexture texture, ref CTransform transform) =>
                    {
                        var states = new RenderStates(transform.SFMLTransform);
                        texture.Draw(Engine.MainWindow, states);
                    });

                    Objects.Iterate((ref CText text, ref CTransform transform) =>
                    {
                        var states = new RenderStates(transform.SFMLTransform);
                        text.Draw(Engine.MainWindow, states);
                    });
                }
                Engine.MainWindow.Display();
                Engine.MainWindow.Clear();

                Time.DeltaTime = (float)( ( DateTime.Now - time ).TotalSeconds );
#if DEBUG
                Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, "Render", ( Time.DeltaTime * 1000 ).ToString());

#endif
                Time.RegisterThreadContinuance(Thread.CurrentThread);
                while (!Time.AreOtherThreadsStillGoing(Thread.CurrentThread))
                    ;
            }
        }
#endif
            }
    /// <summary>
    /// Showcase subsystem, not currently used in examples, primarily used for testing physics implementations. Should not be used.
    /// </summary>
    public class PhysicsSubsystem : Subsystem
    {
        private Vector2u Window = Engine.MainEngine.Settings.WindowDimensions;
        public override void Update(float deltaSeconds)
        {
            Objects.Iterate((ref CPhysicsBody body, ref CTransform transform) =>
            {
                var size = transform.Size / 2;

                body.Velocity += Constants.ModifiableGravity * deltaSeconds;

                if (transform.Position.Y + size.Y > Window.Y || transform.Position.Y < 0)
                {
                    body.Velocity = new Vector2f(body.Velocity.X, -1 * body.Velocity.Y);
                }

                if (transform.Position.X + size.X > Window.X || transform.Position.X < 0)
                {
                    body.Velocity = new Vector2f(-1 * body.Velocity.X, body.Velocity.Y);
                }

                transform.Position += body.Velocity * deltaSeconds;
            });
        }
    }
    /// <summary>
    /// Abstract class for collision subsystems. This type is inherited by the user for creating a custom collision subsystem.
    /// </summary>
    public abstract class CollisionSubsystem : Subsystem
    {
        /// <summary>
        /// Checks if object should be excluded from collision based on data type criteria.
        /// </summary>
        private static readonly Func<CObject, Type[], bool> excludeTypeCheck = (x, exclude) =>
        {
            for (var i = 0; i < exclude.Length; i++)
            {
                if (x.HasDataOf(exclude[i]))
                    return false;
            }
            return true;
        };
        /// <summary>
        /// Checks if object should be included from collision based on data type criteria.
        /// </summary>
        private static readonly Func<CObject, Type, bool> includeTypeCheck = (x, include) =>
        {
            return x.HasDataOf(include);
        };
        /// <summary>
        /// Get collisions of this object. Excluding collisions with objects with any data types in the exclude array.
        /// </summary>
        /// <param name="cObject"></param>
        /// <param name="exclude"></param>
        /// <returns></returns>
        public List<CObject> GetCollisionsOf(CObject cObject, params Type[] exclude)
        {
            unsafe
            {
                return Tree->QueryOverlaps(cObject, x =>
                {
                    return excludeTypeCheck(x, exclude);
                });
            }
        }
        /// <summary>
        /// Get collisions of this object. Including only collisions with objects with the Include data type.
        /// </summary>
        /// <typeparam name="Include"></typeparam>
        /// <param name="cObject"></param>
        /// <returns></returns>
        public List<CObject> GetCollisionsOf<Include>(CObject cObject) where Include : ICData
        {
            unsafe
            {
                return Tree->QueryOverlaps(cObject, x =>
                {
                    return includeTypeCheck(x, typeof(Include));
                });
            }
        }
        /// <summary>
        /// Get the first collision of this object. Excluding collisions with objects with any data types in the exclude array.
        /// </summary>
        /// <param name="cObject"></param>
        /// <param name="exclude"></param>
        /// <returns></returns>
        public CObject GetFirstCollisionOf(CObject cObject, params Type[] exclude)
        {
            unsafe
            {
                return Tree->QueryFirstOverlap(cObject, x =>
                {
                    return excludeTypeCheck(x, exclude);
                });
            }
        }
        /// <summary>
        /// Get the first collision of this object. Including only collisions with objects with the Include data type.
        /// </summary>
        /// <typeparam name="Include"></typeparam>
        /// <param name="cObject"></param>
        /// <returns></returns>
        public CObject GetFirstCollisionOf<Include>(CObject cObject) where Include : ICData
        {
            unsafe
            {
                return Tree->QueryFirstOverlap(cObject, x =>
                {
                    return includeTypeCheck(x, typeof(Include));
                });
            }
        }
        /// <summary>
        /// Updates the collision tree. Must be called.
        /// </summary>
        /// <param name="deltaSeconds"></param>
        public override void Update(float deltaSeconds) => Objects.UpdateCollisionTree();
        public override void Startup()
        {
            base.Startup();
            Priority = 9;
            Parallelable = false;
        }
    }
    /// <summary>
    /// Abstract class for animation subsystems. This type is inherited by the user for creating a custom animation subsystem.
    /// </summary>
    public abstract class AnimationSubsystem : Subsystem
    {
        public override void Startup()
        {
            base.Startup();
            Priority = 8;
            Parallelable = false;
        }
    }
    /// <summary>
    /// Used to intercept UI events. Primarily to report buttons are clicked. This is a standalone type and does not need to be inherited.
    /// </summary>
    public class UISubsystem : Subsystem
    {
        /// <summary>
        /// Checks if any buttons were pressed based on mouse position and mouse button input.
        /// </summary>
        /// <param name="deltaSeconds"></param>
        public override void Update(float deltaSeconds)
        {
            Objects.Iterate((ref CButton button, ref CTransform transform) =>
            {
                if (Input.GetMouseButtonPressed(0) && transform.BoundingBox.Contains(Input.GetMousePosition()))
                    button.Click();
            });
        }
    }

}
namespace ECS.Strings
{
    using static CAllocation;
    /// <summary>
    /// Custom type for creating and using strings in unmanaged environments. Primarily for storing text to be drawn later.
    /// </summary>
    public unsafe struct CString : ICData, IDisposable, IEquatable<string>
    {
        public readonly int Length;
        private IntPtr StringPtr;
        internal readonly int stringHash;
        public CString(string name)
        {
            Length = name.Length;
            StringPtr = Malloc(sizeof(char) * Length);
            MemSet(StringPtr, 0, Length);
            fixed (IntPtr* ptr = &StringPtr)
            {
                var start = (char*)*ptr;
                for (int i = 0; i < Length; i++)
                {
                    *( start + i ) = name[i];
                }
            }
            stringHash = name.GetHashCode();
        }
        public char this[int index]
        {
            get
            {
                if (index < 0 || index >= Length)
                    throw new IndexInvalidException("a CString's character array", index, 0, Length);
                fixed (IntPtr* ptr = &StringPtr)
                {
                    return *( (char*)*ptr + index );
                }
            }
        }
        public void Dispose() => Free(StringPtr);
        public bool Equals(string other) => stringHash == other.GetHashCode();
        public override string ToString()
        {
            string new_string = "";
            for (int i = 0; i < Length; i++)
            {
                new_string += this[i];
            }
            return new_string;
        }
    }
    /// <summary>
    /// Static string extensions.
    /// </summary>
    public static class StringExtensions
    {
        public static string ToIString(this IEnumerable<char> chars)
        {
            string new_string = string.Empty;
            foreach (var ch in chars)
                new_string += ch;
            return new_string;
        }
    }
}
namespace ECS.Graphics
{
    using static RenderWindow;
    using static UnmanagedCSharp;
    using static Maths.RNG;
    /// <summary>
    /// Anchors transforms to specific areas of the screen. Useful for UI elements.
    /// </summary>
    public enum Anchor
    {
        TOP_LEFT = 0,
        TOP_RIGHT = 1,
        BOTTOM_LEFT = 2,
        BOTTOM_RIGHT = 3,
        CENTRE = 4,
    }
    /// <summary>
    /// Static graphics extensions.
    /// </summary>
    public static class GraphicsExtensions
    {
        /// <summary>
        /// Get absolute position of anchor pointer. Relative to window. E.g. TOP_LEFT = (0, 0) and BOTTOM_RIGHT = (Max Window Width, Max Window Height)
        /// </summary>
        /// <param name="anchor"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEnumValueException"></exception>
        public static Vector2f GetAbsoluteFromAnchor(this Anchor anchor)
        {
            var windowDimensions = Engine.MainEngine.Settings.WindowDimensions;
            return anchor switch
            {
                Anchor.TOP_LEFT => new Vector2f(),
                Anchor.TOP_RIGHT => new Vector2f(windowDimensions.X, 0),
                Anchor.BOTTOM_LEFT => new Vector2f(0, windowDimensions.Y),
                Anchor.BOTTOM_RIGHT => new Vector2f(windowDimensions.X, windowDimensions.Y),
                Anchor.CENTRE => new Vector2f(windowDimensions.X / 2, windowDimensions.Y / 2),
                _ => throw new InvalidEnumValueException(typeof(Anchor).Name, (int)anchor)
            };
        }
        /// <summary>
        /// Gets the relative position to an anchor. Get the absolute position of the anchor then add another position to it.
        /// </summary>
        /// <param name="anchor"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        public static Vector2f GetRelativePositionToAnchor(this Anchor anchor, Vector2f position)
        {
            return anchor.GetAbsoluteFromAnchor() + position;
        }
    }
    /// <summary>
    /// Represents the location, size, rotation, scale, origin of an object or sprite. Most, if not all, objects need a transform but this is not enforced by the engine.
    /// </summary>
    public struct CTransform : ICData
    {
        /// <summary>
        /// Typically non-UI elements will use TOP_LEFT.
        /// </summary>
        public Anchor Anchor
        {
            get
            {
                return myAnchor;
            }
            set
            {
                myAnchor = value;
            }
        }
        public Vector2u Size
        {
            get
            {
                return mySize;
            }
            set
            {
                mySize = value;
                myTransformNeedUpdate = true;
                myInverseNeedUpdate = true;
            }
        }
        public Vector2f Position
        {
            get
            {
                if (Anchor == Anchor.TOP_LEFT)
                    return myPosition;
                return GetAnchoredPosition();
            }
            set
            {
                myPosition = value;
                myTransformNeedUpdate = true;
                myInverseNeedUpdate = true;
            }
        }
        public float Rotation
        {
            get
            {
                return myRotation;
            }
            set
            {
                myRotation = value;
                myTransformNeedUpdate = true;
                myInverseNeedUpdate = true;
            }
        }
        public Vector2f Scale
        {
            get
            {
                return myScale;
            }
            set
            {
                myScale = value;
                myTransformNeedUpdate = true;
                myInverseNeedUpdate = true;
            }
        }
        public Vector2f Origin
        {
            get
            {
                return myOrigin;
            }
            set
            {
                myOrigin = value;
                myTransformNeedUpdate = true;
                myInverseNeedUpdate = true;
            }
        }
        /// <summary>
        /// Internal SFML transform (taken from the SFML source code, not my function).
        /// </summary>
        public SFMLTransform SFMLTransform
        {
            get
            {
                if (myTransformNeedUpdate)
                {
                    myTransformNeedUpdate = false;

                    float angle = -myRotation * 3.141592654F / 180.0F;
                    float cosine = (float)Math.Cos(angle);
                    float sine = (float)Math.Sin(angle);
                    float sxc = myScale.X * cosine;
                    float syc = myScale.Y * cosine;
                    float sxs = myScale.X * sine;
                    float sys = myScale.Y * sine;
                    float tx = -myOrigin.X * sxc - myOrigin.Y * sys + Position.X;
                    float ty = myOrigin.X * sxs - myOrigin.Y * syc + Position.Y;

                    myTransform = new SFMLTransform(sxc, sys, tx,
                                                -sxs, syc, ty,
                                                0.0F, 0.0F, 1.0F);
                }
                return myTransform;
            }
        }
        /// <summary>
        /// Internal SFML transform (taken from the SFML source code, not my function).
        /// </summary>
        public SFMLTransform InverseTransform
        {
            get
            {
                if (myInverseNeedUpdate)
                {
                    myInverseTransform = SFMLTransform.GetInverse();
                    myInverseNeedUpdate = false;
                }
                return myInverseTransform;
            }
        }
        private FloatRect GetGlobalBounds() => SFMLTransform.TransformRect(GetLocalBounds());
        private FloatRect GetLocalBounds() => new FloatRect(0, 0, mySize.X, mySize.Y);
        /// <summary>
        /// Checks if this transform intersects another exactly. Rarely use.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool ExpensiveIntersection(CTransform other) => GetGlobalBounds().Intersects(other.GetGlobalBounds());
        public Vector2f GetAnchoredPosition() => myAnchor.GetRelativePositionToAnchor(myPosition);
        internal unsafe bool ExpensiveIntersection(CTransform* other) => GetGlobalBounds().Intersects(other->GetGlobalBounds());
        public CTransform(float x, float y, uint height, uint width, float origin_x, float origin_y, float rotation, Anchor anchor) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(origin_x, origin_y), rotation, anchor) { }
        public CTransform(float x, float y, uint height, uint width, float origin_x, float origin_y, float rotation) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(origin_x, origin_y), rotation, Anchor.TOP_LEFT) { }
        public CTransform(float x, float y, uint height, uint width, float origin_x, float origin_y) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(origin_x, origin_y), 0, Anchor.TOP_LEFT) { }
        public CTransform(float x, float y, uint height, uint width) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(height, width) / 2, 0, Anchor.TOP_LEFT) { }
        public CTransform(Vector2f position, Vector2u size, Vector2f origin, float rotation, Anchor anchor)
        {
            mySize = size;
            myOrigin = origin;
            myPosition = position;
            myRotation = rotation;
            myScale = new Vector2f(1, 1);
            myTransformNeedUpdate = true;
            myInverseNeedUpdate = true;
            myTransform = default;
            myInverseTransform = default;
            myAnchor = anchor;
        }
        public AABB BoundingBox => new AABB(Position.X - myOrigin.X, Position.Y - myOrigin.Y, Position.X + mySize.X - myOrigin.X, Position.Y + mySize.Y - myOrigin.Y);
        private Vector2u mySize;
        private Vector2f myOrigin;
        private Vector2f myPosition;
        private float myRotation;
        private Vector2f myScale;
        private SFMLTransform myTransform;
        private SFMLTransform myInverseTransform;
        private bool myTransformNeedUpdate;
        private bool myInverseNeedUpdate;
        private Anchor myAnchor;
    }
    /// <summary>
    /// Represents a sprite attached to an object. Objects must reference a texture if they are to be drawn. This is a local reference to a global texture entry to reduce RAM usage.
    /// </summary>
    public unsafe struct CTexture : Drawable, ICData
    {
        public static readonly CTexture Null = new CTexture();

        internal int Index;
        internal bool ShouldDraw;
        internal Vertex4 Vertices;
        internal IntPtr TexturePtr;
        internal CTextureEntry* Entry => (CTextureEntry*)TexturePtr;
        public CTexture(FilePath filename)
        {
            TexturePtr = Textures.TryAddTexture(filename);
            Vertices = new Vertex4();
            ShouldDraw = true;
            Index = -1;
            UpdateTexture(TexturePtr);
        }
        public static CTexture CreateSpriteSheet(FilePath filename, Vector2u spriteSize)
        {
            var texture = new CTexture()
            {
                TexturePtr = Textures.NewSpriteSheet(filename, spriteSize),
                Vertices = new Vertex4(),
                ShouldDraw = true,
            };
            texture.UpdateTexture(texture.TexturePtr);
            return texture;
        }
        /// <summary>
        /// Update only the texture coordinates. No need to entirely reset the texture, especially if changed every few frames for animation.
        /// </summary>
        public void UpdateRectOnly()
        {
            var rect = Entry->SheetInfo.GetTextureRectAtIndex(Index);
            var right = rect.Left + rect.Width;
            var bottom = rect.Top + rect.Height;
            Vertices.Vertex0.TexCoords = new Vector2f(rect.Left, rect.Top);
            Vertices.Vertex1.TexCoords = new Vector2f(rect.Left, bottom);
            Vertices.Vertex2.TexCoords = new Vector2f(right, rect.Top);
            Vertices.Vertex3.TexCoords = new Vector2f(right, bottom);
        }
        /// <summary>
        /// Entirely update the texture with new vertices.
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="index"></param>
        public void UpdateTexture(IntPtr texture, int index = 0)
        {
            if (texture != IntPtr.Zero)
            {
                this.Index = index;
                this.TexturePtr = texture;
                var size = Entry->SheetInfo.IndividualSpriteSize;
                var rect = Entry->SheetInfo.GetTextureRectAtIndex(index);
                var right = rect.Left + rect.Width;
                var bottom = rect.Top + rect.Height;
                Vertices.Vertex0 = new Vertex(new Vector2f(0, 0), Color.White, new Vector2f(rect.Left, rect.Top));
                Vertices.Vertex1 = new Vertex(new Vector2f(0, size.Y), Color.White, new Vector2f(rect.Left, bottom));
                Vertices.Vertex2 = new Vertex(new Vector2f(size.X, 0), Color.White, new Vector2f(right, rect.Top));
                Vertices.Vertex3 = new Vertex(new Vector2f(size.X, size.Y), Color.White, new Vector2f(right, bottom));
            }
        }
        public CTexture GetSpriteFromSheet(int index)
        {
            var _this = this;
            _this.UpdateTexture(TexturePtr, index);
            return _this;
        }
        public void SetColor(Color color)
        {
            Vertices.Vertex0.Color = color;
            Vertices.Vertex1.Color = color;
            Vertices.Vertex2.Color = color;
            Vertices.Vertex3.Color = color;
        }
        public CTexture Modify(Ref<CTexture> modifyAction)
        {
            modifyAction(ref this);
            return this;
        }
        public Vector2u GetSize => Entry->SheetInfo.IndividualSpriteSize;
        public Color GetColor() => Vertices.Vertex0.Color;
        /// <summary>
        /// Draws the texture on screen if valid. Can only be drawn from the dedicated render thread.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="states"></param>
        /// <exception cref="RenderThreadCrossDrawingException"></exception>
        public void Draw(RenderTarget target, RenderStates states)
        {
#if SEPARATE_RENDER_THREAD
            if (Thread.CurrentThread != RenderSubsystem.RenderThread)
                throw new RenderThreadCrossDrawingException();
#endif
            if (!ShouldDraw || TexturePtr == IntPtr.Zero)
                return;

            states.CTexture = Entry->TexturePtr;
            ( (RenderWindow)target ).Draw(Vertices, PrimitiveType.TriangleStrip, states);
        }
    }
}
namespace ECS.Maths
{
    /// <summary>
    /// Static mathematical constants. Used with the physics testing implementations. Not primarily used in current examples.
    /// </summary>
    public static class Constants
    {
        public static readonly Vector2f Gravity = new Vector2f(0, 9.80665f);
        public static Vector2f ModifiableGravity = new Vector2f(0, 0);
    }

    /// <summary>
    /// Static global pseudo-random number generator.
    /// </summary>
    public static class RNG
    {
        private static readonly Random Random = new Random();
        public static int Next(int min, int max) => Random.Next(min, max);
    }

    /// <summary>
    /// Static mathematical functions for simple and engine specific operations.
    /// </summary>
    public static class Maths
    {
        /// <summary>
        /// Returns the square distance of 2 vectors. Faster as it does not perform Math.Sqrt.
        /// </summary>
        /// <param name="vector1"></param>
        /// <param name="vector2"></param>
        /// <returns></returns>
        public static float SqDistance(this Vector2f vector1, Vector2f vector2) => (float)( Math.Pow(vector2.X - vector1.X, 2) + Math.Pow(vector2.Y - vector1.Y, 2) );
        /// <summary>
        /// Gets the distance between 2 vectors. Slower as it performs Math.Sqrt. Preferably use vector.SqDistance(vector2) if the actual distance is of no concern.
        /// </summary>
        /// <param name="vector1"></param>
        /// <param name="vector2"></param>
        /// <returns></returns>
        public static float Distance(this Vector2f vector1, Vector2f vector2) => (float)Math.Sqrt(vector1.SqDistance(vector2));
        public static float Dot(this Vector2f vector1, Vector2f vector2)
        {
            var dotVal = 0f;
            dotVal += vector1.X * vector2.X;
            dotVal += vector2.Y * vector2.Y;
            return dotVal;
        }
        public static float SqLength(this Vector2f vector) => (float)( ( vector.X * vector.X ) + ( vector.Y * vector.Y ) );
        public static float Length(this Vector2f vector) => (float)Math.Sqrt(vector.SqLength());
        public static Vector2f Center(this FloatRect box) => new Vector2f(box.Left + ( box.Width / 2 ), box.Top + ( box.Height / 2 ));
        public static Vector2f Normalise(this Vector2f vector)
        {
            var length = vector.Length();
            return new Vector2f(vector.X / length, vector.Y / length);
        }
        public static Vector2f Direction(this Vector2f this_vector, Vector2f target) => ( target - this_vector ).Normalise();
        public static Vector2f Min(this FloatRect box) => new Vector2f(Math.Min(box.Left, box.Left + box.Width), Math.Min(box.Top, box.Top + box.Height));
        public static Vector2f Max(this FloatRect box) => new Vector2f(Math.Max(box.Left, box.Left + box.Width), Math.Max(box.Top, box.Top + box.Height));

        /// <summary>
        /// Custom function to get a rotation from direction. Specific to SFML as 0, 0 is top-left
        /// </summary>
        /// <param name="direction"></param>
        /// <returns></returns>
        public static float ToRotation(this Vector2f direction) => ( (float)( Math.Atan2(direction.Y, direction.X) / ( 2 * Math.PI ) ) * 360f ) + 90f;

        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }

}
namespace ECS.Collections
{
    using static CAllocation;
    /// <summary>
    /// Static class for accessing unmanaged versions of managed C# collections ('System.Collections.Generic'). Very unsafe.
    /// </summary>
    internal static unsafe class Generic
    {
        /// <summary>
        /// Custom array implementation. Used primarily in unmanaged code, specifically for collision trees.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public struct LocalArray<T> : IDisposable where T : unmanaged
        {
            private int sizeOf;
            public int Entries;
            public int MaxSize;
            public int AllocatedBytes;
            private IntPtr Items;
            public LocalArray(int size)
            {
                Entries = 0;
                MaxSize = size;
                sizeOf = sizeof(T);
                AllocatedBytes = sizeOf * size;
                Items = Malloc(AllocatedBytes);
                MemSet(Items, 0, AllocatedBytes);
            }
            /// <summary>
            /// Ensure the index is valid and accessible. Equal to or greater than 0 OR less than MaxSize.
            /// </summary>
            /// <param name="index"></param>
            /// <exception cref="IndexInvalidException"></exception>
            private void ReinforceSize(int index)
            {
                if (index < 0 || index >= MaxSize)
                    throw new IndexInvalidException("a local array", index, 0, MaxSize);
            }
            /// <summary>
            /// (GET) Returns a copy of the data at the index
            /// (SET) Sets the value at the index by reference 
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            public T this[int index]
            {
                get => ReadAt(index);
                set => ModifyAt(index, value);
            }
            /// <summary>
            /// Add a new entry to the array.
            /// </summary>
            /// <param name="item"></param>
            public void Add(T item) => ModifyAt(Entries++, item);
            /// <summary>
            /// Returns the data by reference.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            public T* ReadPointerAt(int index)
            {
                ReinforceSize(index);
                fixed (IntPtr* ptr = &Items)
                {
                    return ( ( (T*)*ptr ) + index );
                }
            }
            /// <summary>
            /// Returns the data by instance.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            private T ReadAt(int index)
            {
                ReinforceSize(index);
                fixed (IntPtr* ptr = &Items)
                {
                    return *( ( (T*)*ptr ) + index );
                }
            }
            /// <summary>
            /// Modify the data at index. Sets it to the new data.
            /// </summary>
            /// <param name="index"></param>
            /// <param name="new_data"></param>
            public void ModifyAt(int index, T new_data)
            {
                ReinforceSize(index);
                fixed (IntPtr* ptr = &Items)
                {
                    *( (T*)*ptr + index ) = new_data;
                }
            }
            /// <summary>
            /// Remove the entry at index and move all entries up to replace it. Similar to List.RemoveAt(int).
            /// </summary>
            /// <param name="index"></param>
            public void RemoveAt(int index)
            {
                ReinforceSize(index);
                fixed (IntPtr* ptr = &Items)
                {
                    T* start = (T*)*ptr;
                    for (int i = index; i < Entries; i++)
                    {
                        start[i] = start[i + 1];
                    }
                    Entries--;
                }
            }
            /// <summary>
            /// Resizes the array by reallocting it in a new block.
            /// </summary>
            /// <param name="size"></param>
            public void Resize(int size)
            {
                Items = CGC.ResizeEntireBlock(Items, sizeOf, MaxSize, size);
                MaxSize = size;
                AllocatedBytes = sizeOf * size;
            }
            /// <summary>
            /// Clears the array by MemSet-ing everything to 0.
            /// </summary>
            public void Clear()
            {
                Entries = 0;
                MemSet(Items, 0, AllocatedBytes);
            }
            /// <summary>
            /// Disposes of the array. Frees its collection.
            /// </summary>
            public void Dispose()
            {
                Free(Items);
            }
        }
        /// <summary>
        /// Simple custom dictionary implementation. Used primarily in unmanaged code, specifically for collision trees.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public struct LocalDictionary<T, U> : IDisposable where T : unmanaged where U : unmanaged
        {
            private struct Entry
            {
                public T key;
                public U value;
                public Entry(T key, U value)
                {
                    this.key = key;
                    this.value = value;
                }
            }
            private LocalArray<Entry> entries;
            public LocalDictionary(int capacity) => entries = new LocalArray<Entry>(capacity);
            /// <summary>
            /// (GET) Returns a copy of the data at the index
            /// (SET) Sets the value at the index by reference
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            /// <exception cref="DictionaryIndexInvalidException"></exception>
            public U this[T index]
            {
                get
                {
                    if (ContainsKey(index) is int _int && _int != -1)
                        return entries[_int].value;
                    throw new DictionaryIndexInvalidException("a local dictionary", index.ToString());
                }
                set
                {
                    if (ContainsKey(index) is int _int && _int != -1)
                    {
                        entries.ReadPointerAt(_int)->value = value;
                        return;
                    }
                    Add(index, value);
                }
            }
            /// <summary>
            /// Checks if the dictionary contains the specified key-value entry. Returns -1 on failure, otherwise returns its index.
            /// </summary>
            /// <param name="key"></param>
            /// <returns></returns>
            public int ContainsKey(T key)
            {
                for (int i = 0; i < entries.Entries; i++)
                {
                    if (entries[i].key.Equals(key))
                    {
                        return i;
                    }
                }
                return -1;
            }
            /// <summary>
            /// Add a new key-value entry.
            /// </summary>
            /// <param name="key"></param>
            /// <param name="value"></param>
            public void Add(T key, U value)
            {
                if (ContainsKey(key) == -1)
                {
                    entries.Add(new Entry(key, value));
                }
            }
            /// <summary>
            /// Removes an existing key-value entry.
            /// </summary>
            /// <param name="key"></param>
            public void Remove(T key)
            {
                if (ContainsKey(key) is int _int && _int != -1)
                {
                    entries.RemoveAt(_int);
                }
            }
            /// <summary>
            /// Resizes the internal array.
            /// </summary>
            /// <param name="size"></param>
            public void Resize(int size) => entries.Resize(size);
            /// <summary>
            /// Disposes of the dictionary.
            /// </summary>
            public void Dispose()
            {
                entries.Dispose();
            }
        }
    }
}
namespace ECS.Physics
{
    /// <summary>
    /// Static physics functions. Used primarily for testing physics implementations. Requires PhysicsSubsystem implementation to do anything. Do not use.
    /// </summary>
    public static class Physics
    {
        /// <summary>
        /// Push a physics body in a direction.
        /// </summary>
        /// <param name="body"></param>
        /// <param name="direction"></param>
        public static void ApplyForce(ref this CPhysicsBody body, Vector2f direction)
        {
            body.Velocity += direction;
        }
    }
    /// <summary>
    /// Represents physics data for handling an object based on physical properties.
    /// </summary>
    public struct CPhysicsBody : ICData
    {
        public static readonly Vector2f ConstantAcceleration = new Vector2f(1, 1);

        public Vector2f Velocity;
        public Vector2f Acceleration;
    }
}
namespace ECS.Collision
{
    using static Maths.Maths;
    using static Collections.Generic;
    /// <summary>
    /// Represents an object that can be collided with. Only contains information about whether it is in an AABB Tree. CTransforms contain the actual AABB information.
    /// </summary>
    public struct CCollider : ICData
    {
        public bool IsInTree;
    }
    /// <summary>
    /// Represents an Axis-Aligned Bounding Box which contains information about the min and max coordinates (corners) of a rectangle.
    /// </summary>
    public struct AABB 
    {
        public float minX;
        public float minY;
        public float maxX;
        public float maxY;
        public float SurfaceArea;

        public override string ToString()
        {
            return minX + " : " + minY + "\n" + maxX + " : " + maxY;
        }

        public AABB(float minx, float miny, float maxx, float maxy)
        {
            this.minX = minx;
            this.minY = miny;
            this.maxX = maxx;
            this.maxY = maxy;
            this.SurfaceArea = ( maxX - minX ) * ( maxY - minY );
        }
        /// <summary>
        /// Checks if this AABB overlaps with another by comparing their min/max corners.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Overlaps(AABB other)
        {
            return maxX > other.minX &&
            minX < other.maxX &&
            maxY > other.minY &&
            minY < other.maxY;
        }
        /// <summary>
        /// Checks if this AABB contains another by comparing their min/max corners.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Contains(AABB other)
        {
            return other.minX >= minX &&
            other.maxX <= maxX &&
            other.minY >= minY &&
            other.maxY <= maxY;
        }
        /// <summary>
        /// Checks if this AABB contains a specific point by comparing min/max corners with the point's X/Y.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public bool Contains(Vector2f point)
        {
            return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
        }
        /// <summary>
        /// Creates a new AABB from 2 AABBs by merging their min/max corners.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public AABB Merge(AABB other)
        {
            return new AABB(
            Min(minX, other.minX), Min(minY, other.minY),
            Max(maxX, other.maxX), Max(maxY, other.maxY));
        }
        /// <summary>
        /// Checks if 2 AABBs intersect. Returns a new AABB representing their intersection.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public AABB Intersection(AABB other)
        {
            return new AABB(
            Max(minX, other.minX), Max(minY, other.minY),
            Min(maxX, other.maxX), Min(maxY, other.maxY));
        }
    }
    /// <summary>
    /// Represents a bounding box attached to the AABB Tree. If this node has a valid object pointer, it is an object, if not, it is a box containing other nodes.
    /// </summary>
    public unsafe struct AABBNode 
    {
        public static readonly int NULL_NODE = int.MaxValue;

        public AABB AssociatedAABB;
        public CObject* ObjectPointer;
        public int ParentNodeIndex;
        public int LeftNodeIndex;
        public int RightNodeIndex;
        public int NextNodeIndex;
        /// <summary>
        /// Checks if this node is a leaf. Leaves have no children and indicate an endpoint to the branch.
        /// </summary>
        /// <returns></returns>
        public bool IsLeaf() => LeftNodeIndex == NULL_NODE;
        /// <summary>
        /// Creates a new empty node. Does not append it to the tree.
        /// </summary>
        /// <returns></returns>
        public static AABBNode New()
        {
            AABBNode node = new AABBNode()
            {
                AssociatedAABB = default,
                ObjectPointer = default,
                ParentNodeIndex = NULL_NODE,
                LeftNodeIndex = NULL_NODE,
                RightNodeIndex = NULL_NODE,
                NextNodeIndex = NULL_NODE,
            };
            return node;
        }
    }
    /// <summary>
    /// Represents the tree containing all the collision data. The nodes of the tree branch into other, isolated, nodes allowing for quick overlapping checks. Broad phase collision testing.
    /// </summary>
    public unsafe struct AABBTree : IDisposable
    {

        private LocalDictionary<long, int> map;
        private LocalArray<AABBNode> nodes;
        private int rootNodeIndex;
        private int allocatedNodeCount;
        private int nextFreeNodeIndex;
        private int nodeCapacity;
        private int growthSize;
        public int transformDataTableIndex;
        public AABBTree(int start_size)
        {
            rootNodeIndex = AABBNode.NULL_NODE;
            allocatedNodeCount = 0;
            nextFreeNodeIndex = 0;
            nodeCapacity = start_size;
            growthSize = start_size;
            nodes = new LocalArray<AABBNode>(start_size);
            map = new LocalDictionary<long, int>(start_size);
            for (int nodeIndex = 0; nodeIndex < start_size; nodeIndex++)
            {
                AABBNode node = AABBNode.New();
                node.NextNodeIndex = nodeIndex + 1;
                nodes.Add(node);
            }
            nodes.ReadPointerAt(start_size - 1)->NextNodeIndex = AABBNode.NULL_NODE;
            transformDataTableIndex = -1;
        }
        /// <summary>
        /// Add a new empty node. Does not add it to the tree. Resizes the node array if there is not enough capacity.
        /// </summary>
        /// <returns></returns>
        private int allocateNode()
        {
            if (nextFreeNodeIndex == AABBNode.NULL_NODE)
            {
                nodeCapacity += growthSize;
                nodes.Resize(nodeCapacity);
                map.Resize(nodeCapacity);
                for (int _nodeIndex = allocatedNodeCount; _nodeIndex < nodeCapacity; _nodeIndex++)
                {
                    AABBNode node = AABBNode.New();
                    node.NextNodeIndex = _nodeIndex + 1;
                    nodes.Add(node);
                }
                nodes.ReadPointerAt(nodeCapacity - 1)->NextNodeIndex = AABBNode.NULL_NODE;
                nextFreeNodeIndex = allocatedNodeCount;
            }
            
            int nodeIndex = nextFreeNodeIndex;
            AABBNode* allocatedNode = nodes.ReadPointerAt(nodeIndex);
            allocatedNode->ParentNodeIndex = AABBNode.NULL_NODE;
            allocatedNode->LeftNodeIndex = AABBNode.NULL_NODE;
            allocatedNode->RightNodeIndex = AABBNode.NULL_NODE;
            nextFreeNodeIndex = allocatedNode->NextNodeIndex;
            allocatedNodeCount++;

            return nodeIndex;
        }
        /// <summary>
        /// Remove a node from the node array. Does not remove it from the tree.
        /// </summary>
        /// <param name="nodeIndex"></param>
        private void deallocateNode(int nodeIndex)
        {
            AABBNode* deallocatedNode = nodes.ReadPointerAt(nodeIndex);
            deallocatedNode->NextNodeIndex = nextFreeNodeIndex;
            nextFreeNodeIndex = nodeIndex;
            allocatedNodeCount--;
        }
        /// <summary>
        /// Inserts a new leaf by finding its closest branch. Merges with its nearest leaf to create a parent node that encapsulates it.
        /// </summary>
        /// <param name="leafNodeIndex"></param>
        private void insertLeaf(int leafNodeIndex)
        {
            if (rootNodeIndex == AABBNode.NULL_NODE)
            {
                rootNodeIndex = leafNodeIndex;
                return;
            }

            int treeNodeIndex = rootNodeIndex;
            AABBNode* leafNode = nodes.ReadPointerAt(leafNodeIndex);
            while (!nodes.ReadPointerAt(treeNodeIndex)->IsLeaf())
            {
                AABBNode* treeNode = nodes.ReadPointerAt(treeNodeIndex);
                int leftNodeIndex = treeNode->LeftNodeIndex;
                int rightNodeIndex = treeNode->RightNodeIndex;
                AABBNode* leftNode = nodes.ReadPointerAt(leftNodeIndex);
                AABBNode* rightNode = nodes.ReadPointerAt(rightNodeIndex);

                AABB combinedAabb = treeNode->AssociatedAABB.Merge(leafNode->AssociatedAABB);

                float newParentNodeCost = 2.0f * combinedAabb.SurfaceArea;
                float minimumPushDownCost = 2.0f * ( combinedAabb.SurfaceArea - treeNode->AssociatedAABB.SurfaceArea );

                float costLeft;
                float costRight;
                if (leftNode->IsLeaf())
                {
                    costLeft = leafNode->AssociatedAABB.Merge(leftNode->AssociatedAABB).SurfaceArea + minimumPushDownCost;
                }
                else
                {
                    AABB newLeftAabb = leafNode->AssociatedAABB.Merge(leftNode->AssociatedAABB);
                    costLeft = ( newLeftAabb.SurfaceArea - leftNode->AssociatedAABB.SurfaceArea ) + minimumPushDownCost;
                }
                if (rightNode->IsLeaf())
                {
                    costRight = leafNode->AssociatedAABB.Merge(rightNode->AssociatedAABB).SurfaceArea + minimumPushDownCost;
                }
                else
                {
                    AABB newRightAabb = leafNode->AssociatedAABB.Merge(rightNode->AssociatedAABB);
                    costRight = ( newRightAabb.SurfaceArea - rightNode->AssociatedAABB.SurfaceArea ) + minimumPushDownCost;
                }

                if (newParentNodeCost < costLeft && newParentNodeCost < costRight)
                {
                    break;
                }

                if (costLeft < costRight)
                {
                    treeNodeIndex = leftNodeIndex;
                }
                else
                {
                    treeNodeIndex = rightNodeIndex;
                }
            }

            int leafSiblingIndex = treeNodeIndex;
            AABBNode* leafSibling = nodes.ReadPointerAt(leafSiblingIndex);
            int oldParentIndex = leafSibling->ParentNodeIndex;
            int newParentIndex = allocateNode();
            AABBNode* newParent = nodes.ReadPointerAt(newParentIndex);
            newParent->ParentNodeIndex = oldParentIndex;
            newParent->AssociatedAABB = leafNode->AssociatedAABB.Merge(leafSibling->AssociatedAABB);
            newParent->LeftNodeIndex = leafSiblingIndex;
            newParent->RightNodeIndex = leafNodeIndex;
            leafNode->ParentNodeIndex = newParentIndex;
            leafSibling->ParentNodeIndex = newParentIndex;

            if (oldParentIndex == AABBNode.NULL_NODE)
            {
                rootNodeIndex = newParentIndex;
            }
            else
            {
                AABBNode* oldParent = nodes.ReadPointerAt(oldParentIndex);
                if (oldParent->LeftNodeIndex == leafSiblingIndex)
                {
                    oldParent->LeftNodeIndex = newParentIndex;
                }
                else
                {
                    oldParent->RightNodeIndex = newParentIndex;
                }
            }
            treeNodeIndex = leafNode->ParentNodeIndex;
            fixUpwardsTree(treeNodeIndex);
        }

        /// <summary>
        /// Removes a leaf and updates its grandparents (if any) with its children.
        /// </summary>
        /// <param name="leafNodeIndex"></param>
        private void removeLeaf(int leafNodeIndex)
        {
            if (leafNodeIndex == rootNodeIndex)
            {
                rootNodeIndex = AABBNode.NULL_NODE;
                return;
            }
            AABBNode* leafNode = nodes.ReadPointerAt(leafNodeIndex);
            if(leafNode->ParentNodeIndex == int.MaxValue)
                return;
            int parentNodeIndex = leafNode->ParentNodeIndex;
            AABBNode* parentNode = nodes.ReadPointerAt(parentNodeIndex);
            int grandParentNodeIndex = parentNode->ParentNodeIndex;
            int siblingNodeIndex = parentNode->LeftNodeIndex == leafNodeIndex ? parentNode->RightNodeIndex : parentNode->LeftNodeIndex;
            AABBNode* siblingNode = nodes.ReadPointerAt(siblingNodeIndex);

            if (grandParentNodeIndex != AABBNode.NULL_NODE)
            {
                AABBNode* grandParentNode = nodes.ReadPointerAt(grandParentNodeIndex);
                if (grandParentNode->LeftNodeIndex == parentNodeIndex)
                {
                    grandParentNode->LeftNodeIndex = siblingNodeIndex;
                }
                else
                {
                    grandParentNode->RightNodeIndex = siblingNodeIndex;
                }
                siblingNode->ParentNodeIndex = grandParentNodeIndex;
                deallocateNode(parentNodeIndex);

                fixUpwardsTree(grandParentNodeIndex);
            }
            else
            {
                rootNodeIndex = siblingNodeIndex;
                siblingNode->ParentNodeIndex = AABBNode.NULL_NODE;
                deallocateNode(parentNodeIndex);
            }

            leafNode->ParentNodeIndex = AABBNode.NULL_NODE;
        }
        /// <summary>
        /// Updates an existing leaf with a new AABB.
        /// </summary>
        /// <param name="leafNodeIndex"></param>
        /// <param name="newAaab"></param>
        private void updateLeaf(int leafNodeIndex, AABB newAaab)
        {
            AABBNode* node = nodes.ReadPointerAt(leafNodeIndex);

            if (node->AssociatedAABB.Contains(newAaab))
            { return; }

            removeLeaf(leafNodeIndex);
            node->AssociatedAABB = newAaab;
            insertLeaf(leafNodeIndex);
        }

        /// <summary>
        /// Fixes the tree to represent any changed AABBs by merging them and setting the new parents' AABB to it.
        /// </summary>
        /// <param name="treeNodeIndex"></param>
        private void fixUpwardsTree(int treeNodeIndex)
        {
            while (treeNodeIndex != AABBNode.NULL_NODE)
            {
                AABBNode* treeNode = nodes.ReadPointerAt(treeNodeIndex);

                AABBNode* leftNode = nodes.ReadPointerAt(treeNode->LeftNodeIndex);
                AABBNode* rightNode = nodes.ReadPointerAt(treeNode->RightNodeIndex);
                treeNode->AssociatedAABB = leftNode->AssociatedAABB.Merge(rightNode->AssociatedAABB);

                treeNodeIndex = treeNode->ParentNodeIndex;
            }
        }
        /// <summary>
        /// Insert a new object into the tree. Its pointer and its transform's AABB are saved to a new node.
        /// </summary>
        /// <param name="cObject"></param>
        public void InsertObject(CObject* cObject)
        {
            int nodeIndex = allocateNode();
            AABBNode* node = nodes.ReadPointerAt(nodeIndex);

            node->AssociatedAABB = cObject->Meta->Get<CTransform>(transformDataTableIndex)->BoundingBox;
            node->ObjectPointer = cObject;

            insertLeaf(nodeIndex);
            map[cObject->ID] = nodeIndex;
        }
        /// <summary>
        /// Remove an object from the tree. This is more expensive if repeatedly called than if the dead object was left in and cleared out in a larger garbage collection later.
        /// </summary>
        /// <param name="cObject"></param>
        public void RemoveObject(CObject cObject)
        {
            if(map.ContainsKey(cObject.ID) is int _int && _int != -1)
            {
                int nodeIndex = _int;
                removeLeaf(nodeIndex);
                deallocateNode(nodeIndex);
                map.Remove(cObject.ID);
            }
        }
        /// <summary>
        /// Update an existing object within the tree with its new AABB.
        /// </summary>
        /// <param name="cObject"></param>
        public void UpdateObject(CObject* cObject)
        {
            int nodeIndex = map[cObject->ID];
            updateLeaf(nodeIndex, cObject->Meta->Get<CTransform>(transformDataTableIndex)->BoundingBox);
        }
        /// <summary>
        /// Return the first object that overlaps with this object.
        /// </summary>
        /// <param name="cObject"></param>
        /// <param name="additionalPredicate"></param>
        /// <returns></returns>
        public CObject QueryFirstOverlap(CObject cObject, Func<CObject, bool> additionalPredicate = null)
        {
            Stack<int> stack = new Stack<int>();
            AABB testAabb = cObject.Meta->Get<CTransform>(transformDataTableIndex)->BoundingBox;
            stack.Push(rootNodeIndex);
            while (stack.Count != 0)
            {
                int nodeIndex = stack.Pop();

                if (nodeIndex == AABBNode.NULL_NODE)
                    continue;

                AABBNode* node = nodes.ReadPointerAt(nodeIndex);

                if (node->AssociatedAABB.Overlaps(testAabb))
                {
                    if (node->IsLeaf() && !node->ObjectPointer->IsNull && !node->ObjectPointer->Equals(cObject))
                    {
                        var cobject = *node->ObjectPointer;
                        if (additionalPredicate(cobject))
                            return cobject;
                    }
                    else
                    {
                        stack.Push(node->LeftNodeIndex);
                        stack.Push(node->RightNodeIndex);
                    }
                }
            }

            return CObject.Null;
        }
        /// <summary>
        /// Return all objects that overlap with this object.
        /// </summary>
        /// <param name="cObject"></param>
        /// <param name="additionalPredicate"></param>
        /// <returns></returns>
        public List<CObject> QueryOverlaps(CObject cObject, Func<CObject, bool> additionalPredicate = null)
        {
            List<CObject> overlaps = new List<CObject>();
            Stack<int> stack = new Stack<int>();
            AABB testAabb = cObject.Meta->Get<CTransform>(transformDataTableIndex)->BoundingBox;
            stack.Push(rootNodeIndex);
            while (stack.Count != 0)
            {
                int nodeIndex = stack.Pop();

                if (nodeIndex == AABBNode.NULL_NODE)
                    continue;

                AABBNode* node = nodes.ReadPointerAt(nodeIndex);

                if (node->AssociatedAABB.Overlaps(testAabb))
                {
                    if (node->IsLeaf() && !node->ObjectPointer->IsNull && !node->ObjectPointer->Equals(cObject))
                    {
                        overlaps.Add(*node->ObjectPointer);
                    }
                    else
                    {
                        stack.Push(node->LeftNodeIndex);
                        stack.Push(node->RightNodeIndex);
                    }
                }
            }

            if (additionalPredicate != null)
                overlaps = overlaps.Where(additionalPredicate).ToList();

            return overlaps;
        }
        /// <summary>
        /// Disposes of the tree, deallocates nodes and sets all object colliders to "Not in the tree".
        /// </summary>
        public void Dispose()
        {
            for(int i = 0; i < nodes.MaxSize; i++)
            {
                if(nodes[i].ObjectPointer != null && nodes[i].ObjectPointer->IsInternalAlive)
                {
                    CCollider* collider = nodes[i].ObjectPointer->GetDataPointer<CCollider>();
                    collider->IsInTree = false;
                }
            }
            map.Dispose();
            nodes.Dispose();
        }
    }
}
namespace ECS.Delegates
{
    /// <summary>
    /// Unmanaged version of C# delegates. Stores the managed delegate and makes marshalled calls to it.
    /// </summary>
    public unsafe struct CDelegate
    {
        private static readonly Dictionary<int, Delegate> delegateMap = new Dictionary<int, Delegate>();

        private int hashCode;
        private IntPtr delegatePtr;
        public object Invoke<T>(params object[] parameters) where T : Delegate
        {
            if(delegatePtr != IntPtr.Zero)
            {
                var dynamic = Marshal.GetDelegateForFunctionPointer(delegatePtr, typeof(T));
                delegatePtr = Marshal.GetFunctionPointerForDelegate(delegateMap[hashCode]);
                return dynamic.DynamicInvoke(parameters);
            }
            return null;
        }
        public static CDelegate New<T>(T action) where T : Delegate
        {
            int hash = action.GetHashCode();
            if(!delegateMap.ContainsKey(hash))
                delegateMap.Add(hash, action);
            var @delegate = new CDelegate
            {
                hashCode = hash,
                delegatePtr = Marshal.GetFunctionPointerForDelegate(action),
            };
            return @delegate;
        }
    }
}
namespace ECS.UI
{
    using static ECS.Collections.Generic;
    using static SFML.Graphics.RenderWindow;
    /// <summary>
    /// UI element. Can be pressed/clicked in order to invoke some action.
    /// </summary>
    public struct CButton : ICData
    {
        private CDelegate OnClick;
        public CButton(Action action)
        {
            OnClick = CDelegate.New(action);
        }
        public void Click()
        {
            OnClick.Invoke<Action>();
        }
    }
    /// <summary>
    /// UI element. Drawable text on the screen such as a textbox.
    /// </summary>
    public struct CText : Drawable, ICData
    {
        private CString String;
        private uint CharacterSize;
        private CFont Font;
        private Color Color;
        private LocalArray<Vertex> vertices;
        private bool needsUpdate;
        public CText(string text, CFont font, uint size = 15)
        {
            String = new CString(text);
            Font = font;
            CharacterSize = size;
            Color = Color.White;
            needsUpdate = true;
            vertices = new LocalArray<Vertex>(String.Length * 6);
        }
        /// <summary>
        /// Clear the current text and change it to the new text
        /// </summary>
        /// <param name="new_text"></param>
        public void ChangeText(string new_text)
        {
            if (new_text.Length == String.Length && new_text.GetHashCode() == String.stringHash)
                return;
            String.Dispose();
            String = new CString(new_text);
            vertices = new LocalArray<Vertex>(String.Length * 6);
            needsUpdate = true;
        }
        /// <summary>
        /// Update the text texture coords for each character in the string. Gets the font data/glyphs using SFML.
        /// (partially taken from the SFML source code, not my function).
        /// </summary>
        private void updateText()
        {
            if (!needsUpdate)
                return;
            needsUpdate = false;
            vertices.Clear();
            if (String.Length == 0)
                return;

            float whiteSpaceWidth = Font.GetGlyph(' ', CharacterSize).Advance;
            float letterSpacing = whiteSpaceWidth / 3f;
            float x = 0;
            float y = CharacterSize;
            uint previousChar = 0;
            for (int i = 0; i < String.Length; i++)
            {
                uint character = String[i];

                if (character == '\r')
                    continue;

                x += Font.GetKerning(previousChar, character, CharacterSize);

                previousChar = character;

                var glyph = Font.GetGlyph(character, CharacterSize);

                float padding = 1f;

                float left = glyph.Bounds.Left - padding;
                float top = glyph.Bounds.Top - padding;
                float right = glyph.Bounds.Left + glyph.Bounds.Width + padding;
                float bottom = glyph.Bounds.Top + glyph.Bounds.Height + padding;

                float u1 = glyph.TextureRect.Left - padding;
                float v1 = glyph.TextureRect.Top - padding;
                float u2 = glyph.TextureRect.Left + glyph.TextureRect.Width + padding;
                float v2 = glyph.TextureRect.Top + glyph.TextureRect.Height + padding;

                vertices.Add(new Vertex(new Vector2f(x + left, y + top), Color, new Vector2f(u1, v1)));
                vertices.Add(new Vertex(new Vector2f(x + right, y + top), Color, new Vector2f(u2, v1)));
                vertices.Add(new Vertex(new Vector2f(x + left, y + bottom), Color, new Vector2f(u1, v2)));
                vertices.Add(new Vertex(new Vector2f(x + left, y + bottom), Color, new Vector2f(u1, v2)));
                vertices.Add(new Vertex(new Vector2f(x + right, y + top), Color, new Vector2f(u2, v1)));
                vertices.Add(new Vertex(new Vector2f(x + right, y + bottom), Color, new Vector2f(u2, v2)));

                x += glyph.Advance + letterSpacing;
            }

        }
        public void Draw(RenderTarget target, RenderStates states)
        {
            updateText();
            unsafe
            {
                states.CTexture = Font.GetTexture(CharacterSize);
                ( (RenderWindow)target ).Draw(vertices.ReadPointerAt(0), (uint)vertices.Entries, PrimitiveType.TriangleStrip, states);
            }
        }
    }
    /// <summary>
    /// UI element. Unmanaged font file used for getting font data when drawing text.
    /// </summary>
    public struct CFont
    {
        public IntPtr sfmlFont;
        public CFont(FilePath filename)
        {
            sfmlFont = Font.sfFont_createFromFile(filename);
        }
        public Glyph GetGlyph(uint characterCode, uint characterSize)
        {
            return Font.sfFont_getGlyph(sfmlFont, characterCode, characterSize, false, 0f);
        }
        public IntPtr GetTexture(uint characterSize)
        {
            return Font.sfFont_getTexture(sfmlFont, characterSize);
        }
        public float GetKerning(uint previousChar, uint nextChar, uint characterSize)
        {
            return Font.sfFont_getKerning(sfmlFont, previousChar, nextChar, characterSize);
        }
    }
}
namespace ECS.Animations
{
    using static UnmanagedCSharp;
    /// <summary>
    /// Represents an animation. Sets the object's texture to the next frame of the animation.
    /// </summary>
    public unsafe struct CAnimation : ICData
    {
        private IntPtr internalEntry;
        internal CAnimationEntry.AnimState animState;

        private CAnimationEntry* Entry => (CAnimationEntry*)internalEntry;
        public uint FrameCount => Entry->FrameCount;
        public CAnimation(string name, uint frame_count)
        {
            internalEntry = Animations.TryAddAnimation(name, frame_count);
            animState = CAnimationEntry.AnimState.Null;
        }
        /// <summary>
        /// Set the texture data to the animation texture data using the index. Does nothing if the index AND texture pointer match current animation state.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="texture"></param>
        public void SetTextureDataTo(int index, ref CTexture texture)
        {
            if (animState.Index == index && animState.Entry == texture.TexturePtr)
                return;

            Entry->SetTexture(index, ref texture, ref this);
        }
        /// <summary>
        /// Set the animation entry's data. This affects all animations that refer to this entry.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="texture"></param>
        public void SetFrameDataTo(int index, ref CTexture texture)
        {
            Entry->SetFrame(index, ref texture);
        }
    }
}
namespace ECS.GarbageCollection
{
    using static CAllocation;
    /// <summary>
    /// Unmanaged Garbage Collection for resizing data blocks. Only used in custom array implementations as data tables use a different method.
    /// </summary>
    public unsafe static class CGC
    {
        /// <summary>
        /// Similar to realloc. Moved here to separate it from CAllocation methods as it should not be used as one.
        /// </summary>
        /// <param name="block"></param>
        /// <param name="size_of"></param>
        /// <param name="original_size"></param>
        /// <param name="new_size"></param>
        /// <returns></returns>
        public static IntPtr ResizeEntireBlock(IntPtr block, int size_of, int original_size, int new_size)
        {
            var new_block = Malloc(size_of * new_size);
            MemSet(new_block, 0, size_of * new_size);
            MemMove(new_block, block, size_of * original_size);
            Free(block);
            return new_block;
        }
    }
}
namespace ECS.Exceptions
{
    using ECS.Logger;
    /// <summary>
    /// Base class for Exceptions in the engine. Used in conjunction with a logger to report why the engine crashed. Detailed, inherited exceptions allow for better debugging.
    /// </summary>
    public abstract class CoreException : Exception
    {
        public CoreException(string message) : base(message + "\nEngine core invalidated.")
        {
#if DEBUG
            Logger.Instance.AddLog(LogKey.Error, GetType().Name, message.Where(x => x != '\n').ToIString());
            Logger.Instance.WriteAllLogs();
#endif
        }
    }
    public class IncompatibleTypeException : CoreException
    {
        public IncompatibleTypeException(string typeName, string desiredTypeName) : base(string.Format("{0} is an incompatible type as it does not inherit {1}", typeName, desiredTypeName)) { } 
    }
    public class InvalidEnumValueException : CoreException
    {
        public InvalidEnumValueException(string enumName, int index) : base(string.Format("The declared enum ({0}) does not contain a definition or switch case clause for the inputted value ({1}).", enumName, index)) { }
    }
    public abstract class TableException : CoreException
    {
        public TableException(string message) : base(message + "\nAn error occurred while handling internal tables.") { }
    }
    public abstract class IndexException : CoreException
    {
        public IndexException(string message) : base(message + "\nAn error occurred while handling indexes.") { }
    }
    public abstract class PointerException : CoreException
    {
        public PointerException(string message) : base(message + "\nAn error occurred while handling internal pointers.") { }
    }
    public abstract class SubsystemException : CoreException
    {
        public SubsystemException(string message) : base(message + "\nAn error occurred while handling a subsystems' events") { }
    }
    public abstract class ThreadException : CoreException
    {
        public ThreadException(string message) : base(message + "\nAn error occurred while handling another thread.") { }
    }
    public abstract class TextureException : CoreException
    {
        public TextureException(string message) : base(message + "\nAn error occurred while handling textures.") { }
    }
    public class DataTableTypeIndexException : TableException
    {
        public DataTableTypeIndexException(int type) : base(string.Format("The main Lookup table does not contain a type definition or internal table for the desired type ({0}). Please add it using Subsystem.AddNewDataType<T>().", type)) { }
    }
    public class LookupTableLimitException : TableException
    {
        public LookupTableLimitException(int limit) : base(string.Format("You have exceeded the table entry limit ({0}) in the main Lookup table. Please allocated more space in the Engine's settings to proceed.", limit)) { }
    }
    public class LinkedTableNullException : TableException
    {
        public LinkedTableNullException(int index, int tableCount) : base(string.Format("The table index ({0}) was invalid as the hook only contains {1} tables linked together.", index, tableCount)) { }
    }
    public class LinkedTableNotNullException : TableException
    {
        public LinkedTableNotNullException(int index) : base(string.Format("The table index ({0}) was valid but not the last valid index, the search algorithm was looking for the last index and failed.", index)) { }
    }
    public class IndexInvalidException : IndexException
    {
        public IndexInvalidException(string dataName, int index, int lowerBound, int upperBound) : base(string.Format("While attempting to access {0}, an invalid indexer ({1}) was used as it was outside the limits of the lower ({2}) and upper bounds ({3}) of the table.", dataName, index, lowerBound, upperBound)) { }
    }
    public class DictionaryIndexInvalidException : IndexException
    {
        public DictionaryIndexInvalidException(string dicName, string index) : base(string.Format("While attempting to access {0}, an invalid indexer ({1}) was used as it was not present in the dictionary.", dicName, index)) { }
    }
    public class ObjectPointerNullException : PointerException
    {
        public ObjectPointerNullException() : base("The object you are trying to access does not exist.") { }
    }
    public class DataPointerNullException : PointerException
    {
        public DataPointerNullException(string typeName) : base(string.Format("Object could not access {0} related data as it did not possess any. Please check the object is valid and you added the relevant data to it.", typeName)) { }
        public DataPointerNullException(string typeName, string typeName2) : base(string.Format("Object could not access {0} and/or {1} related data as it did not possess any. Please check the object is valid and you added the relevant data to it.", typeName, typeName2)) { }
    }
    public class MissingReferenceException : PointerException
    {
        public MissingReferenceException() : base("The object was already destroyed and this reference is no longer valid.") { }
    }
    public class TextureNotFoundException : TextureException
    {
        public TextureNotFoundException(string filename) : base(string.Format("The loaded texture at {0} could not be found. Either the path is wrong or the texture was not previously loaded.", filename)) { }
    }
    public class TextureSpriteSheetInvalidException : TextureException
    {
        public TextureSpriteSheetInvalidException() : base("This texture is not a sprite sheet and could not be partitioned.") { }
    }
    public class SubsystemNotImplementedException : SubsystemException
    {
        public SubsystemNotImplementedException(string subSystemName) : base(string.Format("An implementation of the {0} subsystem was not provided despite the Engine settings requiring one.", subSystemName)) { }
    }
    public class SubsystemImplementedException : SubsystemException
    {
        public SubsystemImplementedException(string subSystemName) : base(string.Format("An implementation of the {0} subsystem was provided despite the Engine settings not requiring one.", subSystemName)) { }
    }
    public class DisallowedSubsystemAddFlagException : SubsystemException
    {
        public DisallowedSubsystemAddFlagException(string name) : base(string.Format("The subsystem flags are already set, you are not allowed to add: {0}", name)) { }
    }
    public class RenderThreadAlreadyExistsException : ThreadException
    {
        public RenderThreadAlreadyExistsException() : base("The render thread was already previously created.") { }
    }
    public class RenderThreadCrossDrawingException : ThreadException
    {
        public RenderThreadCrossDrawingException() : base("Another thread, other than the designated render thread, attempted to draw to the screen.") { }
    }
}
namespace ECS.Logger
{
    /// <summary>
    /// Logs information about the running engine. Used for statistics gathering and debugging. Only one logger can exist at once. Does nothing in Release builds.
    /// </summary>
    public class Logger
    {
        internal static Logger Instance { get; private set; } = null;

        private static readonly object SyncRoot = new object();
        private const string LoggerExtension = ".tempLog";

        private readonly List<string> Logs = new List<string>();

        public readonly bool IsFileRelative = true;
        public readonly string LogFileName = "Log";

        private DateTime lastLog = new DateTime();
        private string lastLogName = string.Empty;

        public string GetFileLocation()
        {
            if (IsFileRelative)
                return Directory.GetCurrentDirectory() + "\\" + LogFileName + LoggerExtension;
            return LogFileName + LoggerExtension;
        }

        public void WriteAllLogs()
        {
            lock (SyncRoot)
            {
                File.AppendAllLines(GetFileLocation(), Logs);
                Logs.Clear();
            }
        }

        private void ConstructLogsFromFile()
        {
            lock(SyncRoot)
            {
                foreach (var item in File.ReadAllLines(GetFileLocation()))
                {
                    AddLog(LogItem.FromString(item));
                }
            }
        }
        private Logger(string filename, bool relativeFileName = true, bool deleteOld = true)
        {
            LogFileName = filename;
            IsFileRelative = relativeFileName;

            if(File.Exists(GetFileLocation()))
            {
                if (deleteOld)
                    File.Delete(GetFileLocation());
                else
                    ConstructLogsFromFile();
            }
        }

        private void AddLog(string log)
        {
            lock (SyncRoot)
            {
                Logs.Add(log);
            }
        }
        public void AddLog(LogItem item) => this.AddLog(item.ToString());
        public void AddLog(LogKey key, string name, string value) => this.AddLog(new LogItem(key, name, value));
        internal static void CreateLogger(string filename) => Instance = new Logger(filename);

        /// <summary>
        /// Display the results of the available logs. Shows Average, Lowest and Highest values for each entry type.
        /// </summary>
        /// <param name="inCurrentDirectory"></param>
        /// <param name="otherPath"></param>
        public static void ReadAvailableLogs(bool inCurrentDirectory = true, string otherPath = "")
        {
            var path = inCurrentDirectory ? Directory.GetCurrentDirectory() : otherPath;
            foreach (var file in Directory.GetFiles(path))
            {
                if (file.Contains(LoggerExtension))
                {
                    Console.WriteLine("\n" + file.Replace(path, ""));
                    var logger = new Logger(Path.GetFileNameWithoutExtension(file), inCurrentDirectory, false);
                    var averages = new Dictionary<string, List<double>>();
                    var lowests = new Dictionary<string, double>();
                    var highests = new Dictionary<string, double>();
                    foreach(var item in logger.Logs)
                    {
                        var logItem = LogItem.FromString(item);
                        if (logItem.Key == LogKey.StatisticsLog)
                        {
                            var double_val = Convert.ToDouble(logItem.Value);

                            if (!lowests.ContainsKey(logItem.Name))
                                lowests.Add(logItem.Name, double_val);
                            else if (lowests[logItem.Name] > double_val)
                                lowests[logItem.Name] = double_val;


                            if (!highests.ContainsKey(logItem.Name))
                                highests.Add(logItem.Name, double_val);
                            else if (highests[logItem.Name] < double_val)
                                highests[logItem.Name] = double_val;

                            if (averages.ContainsKey(logItem.Name))
                                averages[logItem.Name].Add(double_val);
                            else
                                averages.Add(logItem.Name, new List<double> { double_val });
                        }
                    }

                    Console.WriteLine("Averages");
                    foreach(var kvp in averages)
                    {
                        Console.Write(kvp.Key + ": ");
                        var value = 0d;
                        foreach (var item in kvp.Value)
                            value += item;
                        Console.WriteLine(value / kvp.Value.Count);
                    }

                    Console.WriteLine("\nLowests");
                    lowests.PrintDict();
                    Console.WriteLine("\nHighests");
                    highests.PrintDict();
                    Console.WriteLine();
                }
            }
        }
    }
    public enum LogKey
    {
        Default = 0,
        DebugLog,
        StatisticsLog,
        Error,
    }
    /// <summary>
    /// Logs are written in a specific way for easy decoding, this is used to organise and compile logs from strings.
    /// </summary>
    public struct LogItem
    {
        private static readonly string[] SeparatorArray = new string[] { SeparatorString };
        public const string SeparatorString = ":::";

        public LogKey Key;
        public string Name;
        public string Value;
        public long TimeStamp;

        public LogItem(LogKey key, string name, string value)
        {
            Key = key;
            Name = name;
            Value = value;
            TimeStamp = DateTime.Now.Ticks;
            if (key is LogKey.DebugLog)
                Debug.Log(this);
        }

        public override string ToString()
        {
            return Key.ToString() + SeparatorString + TimeStamp.ToString() + SeparatorString + Name + SeparatorString + Value;
        }

        private static readonly Dictionary<string, LogKey> EnumKeyDict = new Dictionary<string, LogKey>();
        /// <summary>
        /// Create a log item from a specific string. Will fail if string is not formatted correctly.
        /// </summary>
        /// <param name="loggedItem"></param>
        /// <returns></returns>
        public static LogItem FromString(string loggedItem)
        {
            if (EnumKeyDict.Count == 0)
                foreach (var entry in (LogKey[])Enum.GetValues(typeof(LogKey)))
                    EnumKeyDict.Add(entry.ToString(), entry);

            string[] split = loggedItem.Split(SeparatorArray, StringSplitOptions.None);
            var item = new LogItem
            {
                Key = EnumKeyDict[split[0]],
                Name = split[2],
                Value = split[3],
                TimeStamp = Convert.ToInt64(split[1])
            };
            return item;
        }
    }
    /// <summary>
    /// Static logger extensions.
    /// </summary>
    public static class LoggerExtensions
    {
        public static void PrintDict<Key, Value>(this Dictionary<Key, Value> dict)
        {
            foreach (var kvp in dict)
            {
                Console.Write(kvp.Key + ": ");
                Console.WriteLine(kvp.Value);
            }
        }
    }

}