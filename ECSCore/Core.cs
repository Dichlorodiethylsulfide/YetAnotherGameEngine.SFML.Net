#define SEPARATE_RENDER_THREAD
//#undef SEPARATE_RENDER_THREAD

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

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
    public static class CTime
    {
        private static readonly object TimeSyncRoot = new object();
        private struct FrameTimeDelay
        {
            public float DeltaTime0;
            public float DeltaTime1;

            public Thread RenderThread;
            public bool HasRenderUpdatedRecently;

            public Thread GameLoopThread;
            public bool HasGameLoopUpdatedRecently;
        }
        private static FrameTimeDelay FrameTime = new FrameTimeDelay();
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
        internal static bool AreOtherThreadsStillGoing(Thread thread) // check the other thread's activity
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
        internal static void SetRenderThread(Thread thread)
        {
            FrameTime.RenderThread = thread;
        }
        internal static void SetGameLoopThread(Thread thread)
        {
            FrameTime.GameLoopThread = thread;
        }
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
        public static float FPS { get; internal set; } = 0f;
    }
    public interface IComponentData { }
    public static class Debug
    {
        public static void Log<T>(T loggable) => Console.WriteLine(loggable.ToString());
        public static void Breakpoint() => Console.WriteLine("Hit a breakpoint.");
    }
    public unsafe struct ByRefData<T> where T : unmanaged
    {
        public static readonly ByRefData<T> Null = new ByRefData<T>();

        private T* ptr;
        internal ByRefData(CObject* cObject)
        {
            if (!typeof(IComponentData).IsAssignableFrom(typeof(T)))
                throw new IncompatibleTypeException(typeof(T).Name, typeof(IComponentData).Name);
            ptr = cObject->GetDataPointer<T>();
        }
        public T VolatileRead() => *ptr;
        public void VolatileWrite(Ref<T> action) => action(ref *ptr);
    }
    public unsafe struct ByRefObject
    {
        public static readonly ByRefObject Null = new ByRefObject();
        private CObject* ptr;

        internal ByRefObject(CObject* ptr)
        {
            this.ptr = ptr;
        }
        internal CObject* VolatilePtr() => ptr;
        public CObject VolatileRead() => *ptr;
        public void VolatileWrite<T>(Ref<T> action) where T : unmanaged => action(ref *ptr->GetDataPointer<T>());
    }
    public struct CObject : IEquatable<CObject>, IDisposable
    {
        private static long incrementer = 1;
        public static readonly CObject Null = new CObject();

        internal long Index;
        internal bool IsAlive;

        internal IntPtr MetaPtr;
        internal unsafe MetaData* Meta
        {
            get
            {
                fixed(IntPtr* ptr = &MetaPtr)
                {
                    return (MetaData*)*ptr;
                }
            }
        }
        public bool HasDataOf<T>() where T : unmanaged => this.HasDataOf(LookupTable.GetDataTableIndex<T>());
        internal unsafe bool HasDataOf(int index)
        {
            if(!IsActiveNull())
                return Meta->HasDataOf(index);
            return false;
        }
        internal unsafe bool HasDataOfAll(params int[] indexes)
        {
            if(!IsActiveNull())
                return Meta->HasDataOfAll(indexes);
            return false;
        }
        public long ID { get; private set; }
        public void AddData<T>(T data) where T : unmanaged
        {
            if (!HasDataOf<T>())
                Objects.AddObjectData(this, data);
        }
        internal unsafe T* GetDataPointer<T>() where T : unmanaged
        {
            var index = LookupTable.GetDataTableIndex<T>();
            if (HasDataOf(index))
            {
                unsafe
                {
                    return Meta->Get<T>(index);
                }
            }
            throw new DataPointerNullException(typeof(T).Name);
        }
        public T GetData<T>() where T : unmanaged
        {
            var index = LookupTable.GetDataTableIndex<T>();
            if (HasDataOf(index))
            {
                unsafe
                {
                    return *Meta->Get<T>(index);
                }
            }
            //return default;
            throw new DataPointerNullException(typeof(T).Name);
        }
        public void WriteData<T>(Ref<T> action) where T : unmanaged
        {
            var index = LookupTable.GetDataTableIndex<T>();
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
        public void WriteData2<T, U>(RefRef<T, U> action) where T : unmanaged where U : unmanaged
        {
            var index = LookupTable.GetDataTableIndex<T>();
            var index2 = LookupTable.GetDataTableIndex<U>();
            if(HasDataOf(index) && HasDataOf(index2))
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
        public void OverwriteData<T>(T data) where T : unmanaged
        {
            var index = LookupTable.GetDataTableIndex<T>();
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
        /// Used to avoid AccessViolation and NullReference Exceptions
        /// </summary>
        /// <returns>False if object can be used, so !IsActiveNull() is the correct existence check, otherwise True</returns>
        public unsafe bool IsActiveNull() => !IsAlive || this.ID <= 0 || MetaPtr == IntPtr.Zero || Meta->MaxSize == 0;
        public override int GetHashCode() => (int)this.ID;
        public bool Equals(CObject other) => this.ID == other.ID;
        public void Dispose()
        {
            Destroy(this);
        }
        
        internal void InternalDispose()
        {
            unsafe
            {
                Meta->Dispose();
            }
            Free(MetaPtr);
            MetaPtr = IntPtr.Zero;
            ID = 0;
            Index = -1;
            IsAlive = false;
        }
        public static void Destroy(ref CObject obj)
        {
            Destroy(obj);
            obj = Null;
        }
        public static void Destroy(CObject obj)
        {
            unsafe
            {
                lock(Subsystem.SyncRoot)
                {
                    Objects.RemoveObjectAtIndex((int)obj.Index);
                    obj.InternalDispose();
                }
            }
        }
        internal static CObject New(long index)
        {
            var obj = new CObject
            {
                ID = index,
                Index = index - 1,
                MetaPtr = MetaData.New(),
                IsAlive = true
            };
            return obj;
        }
        public static CObject New() => Objects.AddNewObject(incrementer++);
    }
    internal static class CAllocation
    {
        private const string ECSDLL = "ECSDLL";

        [DllImport(ECSDLL)]
        public static extern IntPtr Malloc(int size);
        [DllImport(ECSDLL)]
        public static extern IntPtr Realloc(IntPtr ptr, int size);
        [DllImport(ECSDLL)]
        public static extern void Free(IntPtr ptr);
        [DllImport(ECSDLL)]
        public static extern void MemSet(IntPtr ptr, int value, int size);
        [DllImport(ECSDLL)]
        public static extern void MemMove(IntPtr destination, IntPtr source, int size);
    }
    public static unsafe class UnmanagedCSharp
    {
        private const int DataTableStartIndex = 3; // Object Table at 0, Texture table will be 1, collisions is 2, animations will be 3, so should be 4 soon
        private static int DefaultDataTableSize = 0;
        private static IntPtr Table = IntPtr.Zero;
        internal static LookupTable* TablePtr => (LookupTable*)Table;
        internal static int DataTableEntries => TablePtr->Entries - DataTableStartIndex;
        internal static Hook* ObjectTablePtr => (Hook*)*&TablePtr->Value0;
        internal static Hook* TextureTablePtr => (Hook*)*( ( &TablePtr->Value0 ) + 1 );
        internal static AABBTree* Tree => (AABBTree*)*( ( &TablePtr->Value0 ) + 2 );
        public static void Entry(int mainSize = 64, int objectSize = 1024, int defaultDataSize = 1024, int textureSize = 64)
        {
            LookupTable.CreateTable(mainSize + DataTableStartIndex);
            LookupTable.AddNewType(CObject.Null, objectSize);
            LookupTable.AddNewType(TextureEntry.Null, textureSize);
            LookupTable.CreateCollisionTree(objectSize);
            DefaultDataTableSize = defaultDataSize;
            LookupTable.AddNewDataType<Texture>();
            LookupTable.AddNewDataType<Transform>();
            LookupTable.AddNewDataType<CString>();
            LookupTable.AddNewDataType<Button>();
            LookupTable.AddNewDataType<Text>();
            Tree->transformDataTableIndex = LookupTable.GetDataTableIndex<Transform>();
            
        }
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
        internal struct LookupTable
        {
            private static readonly Dictionary<Type, int> DataTableLookup = new Dictionary<Type, int>();

            public int Entries;
            public int MaxSize;
            public int AllocatedBytes;
            public IntPtr Value0;

            internal void Add(IntPtr ptr)
            {
                if (Entries >= MaxSize)
                    throw new LookupTableLimitException(MaxSize);
                fixed (IntPtr* ptrPtr = &Value0)
                {
                    *( ptrPtr + Entries++ ) = ptr;
                }
            }

            private int getDataTable(int type)
            {
                fixed(IntPtr* ptrPtr = &Value0)
                {
                    for (int i = DataTableStartIndex; i < Entries; i++)
                    {
                        var hook = (Hook*)*(ptrPtr + i);
                        if (hook->DataType == type)
                            return i;
                    }
                    throw new DataTableTypeIndexException(type);
                }
            }

            private void addNewType<T>(T empty, bool is_data_type, int size = 1024) where T : unmanaged
            {
                Hook.NewTableChain<T>(size);
                if(is_data_type)
                    DataTableLookup.Add(typeof(T), getDataTable(typeof(T).GetHashCode()));
            }

            private Hook* getDataTableFromIndex(int index)
            {
                fixed (IntPtr* ptrPtr = &Value0)
                {
                    if (index < 0 || index >= TablePtr->MaxSize)
                        throw new IndexInvalidException("Data Type tables", index, 0, TablePtr->MaxSize);
                    return (Hook*)*( ptrPtr + index );
                }
            }

            public static void AddNewDataType<T>() where T : unmanaged
            {
                if(!typeof(IComponentData).IsAssignableFrom(typeof(T)))
                {
                    throw new IncompatibleTypeException(typeof(T).Name, typeof(IComponentData).Name);
                }
                TablePtr->addNewType(new T(), true, DefaultDataTableSize);
            }

            public static void AddNewType<T>(T empty, int size = 1024) where T : unmanaged => TablePtr->addNewType(empty, false, size);

            public static int GetDataTableIndex<T>()
            {
                if (DataTableLookup.TryGetValue(typeof(T), out var value))
                    return value;
                throw new DictionaryIndexInvalidException("DataTableLookup", typeof(T).Name);
            }
            public static Hook* GetDataTable<T>() where T : unmanaged
            {
                return TablePtr->getDataTableFromIndex(GetDataTableIndex<T>());
            }
            internal static Hook* GetDataTableFromIndex(int index)
            {
                return TablePtr->getDataTableFromIndex(index);
            }

            internal static void CreateTable(int size)
            {
                var bytes = ( sizeof(int) * 3 ) + ( sizeof(IntPtr) * size );
                Table = Malloc(bytes);
                MemSet(Table, 0, bytes);
                var table = (LookupTable*)Table;
                *table = new LookupTable();
                table->Entries = 0;
                table->MaxSize = size;
                table->AllocatedBytes = bytes;
            }

            internal static void CreateCollisionTree(int size)
            {
                var bytes = sizeof(AABBTree);
                var entry = Malloc(bytes);
                MemSet(entry, 0, bytes);
                var tree = (AABBTree*)entry;
                *tree = new AABBTree(size);
                TablePtr->Add(entry);
            }
        }

        public delegate bool PredicateIn<T>(in T t);
        public delegate void Ref<T0>(ref T0 data0);
        public delegate void RefWithObject<T0, O0>(ref T0 data0, ref O0 object0);
        public delegate void RefRef<T0, T1>(ref T0 data0, ref T1 data1);
        public delegate void RefWithObject<T0, T1, O0>(ref T0 data0, ref T1 data1, ref O0 object0);
        public delegate void RefRefRef<T0, T1, T2>(ref T0 data0, ref T1 data1, ref T2 data2);
        public delegate void In<T0>(in T0 data0);
        public delegate bool Compare<T0>(in T0 data);

        internal struct ParallelHookIterator<T> where T : unmanaged
        {
            private Hook* internalHook;
            private int currentIndex;
            private int totalReturned;
            public int expectedLimit;
            private bool limitReached;
            private Predicate<T> predicateCheck;
            private LinkableTable* currentTable;
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
            public void Reset()
            {
                currentTable = internalHook->Start;
                currentIndex = 0;
                totalReturned = 0;
                limitReached = expectedLimit > 0 ? false : true;
            }

            public static ParallelHookIterator<CObject> GetIterator(int expectedLimit, Predicate<CObject> check)
            {
                var iterator = new ParallelHookIterator<CObject>();
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
        internal struct HookIterator<T> where T : unmanaged
        {
            private Hook* internalHook;
            private int currentIndex;
            private int totalReturned;
            public int expectedLimit;
            private bool limitReached;
            private Predicate<T> predicateCheck;
            private LinkableTable* currentTable;

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
            public void Reset()
            {
                currentTable = internalHook->Start;
                currentIndex = 0;
                totalReturned = 0;
                limitReached = expectedLimit > 0 ? false : true;
            }

            public static HookIterator<CObject> GetIterator(int expectedLimit, Predicate<CObject> check)
            {
                var iterator = new HookIterator<CObject>();
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

        internal struct Hook
        {
            public int SizeOfData;
            public int DataType;
            public int Count;
            public int TableSize;
            public IntPtr Empty;
            public LinkableTable* Start;
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
            private CTuple<int, int> GetTableAndEntryIndexes(int index)
            {
                var tableIndex = index / TableSize;
                var entryIndex = index - ( tableIndex * TableSize );
                return new CTuple<int, int>(tableIndex, entryIndex);
            }
            private LinkableTable* GetTable<T>(int index) where T : unmanaged
            {
                var table = Start;
                for (int i = 0; i < index; i++)
                    table = table->Next;
                if (table == null)
                    throw new LinkedTableNullException(index, Count);
                return table;
            }

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
            public T Find<T>(PredicateIn<T> predicate) where T : unmanaged
            {
                var item = FindRef(predicate);
                if (item == null)
                    throw new ObjectPointerNullException();
                return *item;
            }

            public T* GetRef<T>(int index) where T : unmanaged
            {
                var tuple = GetTableAndEntryIndexes(index);
                return GetTable<T>(tuple.Item1)->GetRef<T>(tuple.Item2);
            }

            public T Get<T>(int index) where T : unmanaged => *GetRef<T>(index);

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
            private void NewTable<T>(LinkableTable* table) where T : unmanaged
            {
                table->Next = LinkableTable.NewTable(table, TableSize, new T());
                Count++;
            }
            public void AppendNewTable<T>() where T : unmanaged
            {
                var table = Start;
                while (table->Next != null)
                    table = table->Next;
                NewTable<T>(table);
            }

            public void DebugHook<T>() where T : unmanaged
            {
                var total = 0;
                var table = Start;
                for (int i = 0; i < Count; i++)
                {
                    total += table->DebugTable(new T());
                    table = table->Next;
                }
                Console.WriteLine("Total Tables: " + Count);
                Console.WriteLine("Total Objects: " + total);
            }

            public static void NewTableChain<T>(int size = 1024) where T : unmanaged
            {
                var outer = Malloc(sizeof(Hook));
                MemSet(outer, 0, sizeof(Hook));
                var hook = (Hook*)outer;
                hook->Empty = Malloc(sizeof(T));
                MemSet(hook->Empty, 0, sizeof(T));
                hook->DataType = typeof(T).GetHashCode();
                hook->SizeOfData = sizeof(T);
                hook->Count = 1;
                hook->TableSize = size;
                hook->Start = LinkableTable.NewTable(null, size, new T());
                TablePtr->Add(outer);
            }
        }
        internal struct LinkableTable
        {
            public int ReadIndex;
            public int WriteIndex;
            public int Count;
            public int MaxSize;
            public int AllocatedBytes;
            public LinkableTable* Previous;
            public LinkableTable* Next;
            public byte Value0;

            public int DebugTable<T>(T empty) where T : unmanaged
            {
                fixed (byte* ptr = &Value0)
                {
                    var counter = 0;
                    for (int i = 0; i < MaxSize; i++)
                    {
                        var Object = (T*)ptr + i;
                        if (!Object->Equals(empty))
                            counter++;
                    }
                    Console.WriteLine(counter + " / " + Count);
                    return counter;
                }
            }

            public void ClearTable<T>(T empty) where T : unmanaged
            {
                fixed (byte* ptr = &Value0)
                {
                    for (int i = 0; i < MaxSize; i++)
                    {
                        *( (T*)ptr + i ) = empty;
                    }
                }
            }

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
            public bool TryGetRef<T>(T empty, int index, out T* ptr) where T : unmanaged
            {
                ptr = GetRef<T>(index);
                return !ptr->Equals(empty);
            }
            public T* GetRef<T>(int index) where T : unmanaged
            {
                if (index < 0 || index >= MaxSize)
                    throw new IndexInvalidException("a Hooks' linked tables", index, 0, MaxSize);
                fixed (byte* ptr = &Value0)
                {
                    return (T*)ptr + index;
                }
            }
            public T Get<T>(int index) where T : unmanaged
            {
                return *GetRef<T>(index);
            }

            public static LinkableTable* NewTable<T>(LinkableTable* previous, int size, T empty) where T : unmanaged
            {
                var bytes = ( sizeof(int) * 5 ) + sizeof(IntPtr) + ( sizeof(LinkableTable*) * 2 ) + ( sizeof(T) * size );
                var entry = Malloc(bytes);
                MemSet(entry, 0, bytes);
                var table = (LinkableTable*)entry;
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
        /*
        internal struct SpriteInfo
        {
            public bool IsSpriteSheet;
            public int SpriteCount;
            public int SpriteIndex;
            public Vector2u SpriteSize;
            public Vector2u IndividualSpriteSize;
            public IntRect TextureRect;
            public static SpriteInfo NormalSprite(Vector2u spriteSize)
            {
                var info = new SpriteInfo()
                {
                    IsSpriteSheet = false,
                    SpriteCount = 0,
                    SpriteIndex = 0,
                    SpriteSize = spriteSize,
                    TextureRect = new IntRect(0, 0, (int)spriteSize.X, (int)spriteSize.Y),
                };
                return info;
            }
            public static SpriteInfo SpriteSheet(Vector2u totalSheetSize, Vector2u spriteSize)
            {
                var info = new SpriteInfo()
                {
                    IsSpriteSheet = true,
                    SpriteCount = (int)Math.Max(totalSheetSize.X / spriteSize.X, totalSheetSize.Y / spriteSize.Y), // check this
                    SpriteIndex = -1,
                    SpriteSize = totalSheetSize,
                    IndividualSpriteSize = spriteSize,
                    TextureRect = new IntRect(0, 0, (int)totalSheetSize.X, (int)totalSheetSize.Y),
                };
                return info;
            }
            public static SpriteInfo SpriteFromSheet(int index, Vector2u spriteSize)
            {
                var info = new SpriteInfo()
                {
                    IsSpriteSheet = false,
                    SpriteCount = -1,
                    SpriteIndex = index,
                    SpriteSize = spriteSize,
                    TextureRect = new IntRect(index * (int)spriteSize.X, 0, (int)spriteSize.X, (int)spriteSize.Y),
                };
                return info;
            }
        }
        */
        internal struct SpriteSheetInfo
        {
            private struct IntRectIndex
            {
                public int Index;
                public IntRect Rect;
            }
            public int SpriteCount;
            public Vector2u TotalSize;
            public Vector2u IndividualSpriteSize;
            public IntPtr IntRects;
            public SpriteSheetInfo(int count, Vector2u total, Vector2u individual)
            {
                SpriteCount = count;
                TotalSize = total;
                IndividualSpriteSize = individual;
                IntRects = Malloc(count * sizeof(IntRectIndex));
                MemSet(IntRects, 0, count * sizeof(IntRectIndex));
                fixed (IntPtr* ptr = &IntRects)
                {
                    var int_rect = (IntRectIndex*)*ptr;
                    for(int i = 0; i < count; i++)
                    {
                        var index = int_rect + i;
                        index->Index = i;
                        index->Rect = new IntRect(i * (int)individual.X, 0, (int)individual.X, (int)individual.Y);
                    }
                }
            }
            public IntRect GetTextureRectAtIndex(int index)
            {
                if (index < 0 || index >= SpriteCount)
                    throw new IndexInvalidException("a sprite sheet", index, 0, SpriteCount);
                fixed (IntPtr* ptr = &IntRects)
                {
                    return ( ( (IntRectIndex*)*ptr ) + index )->Rect;
                }
            }
        }
        internal struct TextureEntry : IDisposable
        {
            public static readonly TextureEntry Null = new TextureEntry();
            public int HashCode;
            public SpriteSheetInfo SheetInfo;
            public IntPtr TexturePtr;
            private Vector2u GetSizeDirectly() => SFMLTexture.sfTexture_getSize(TexturePtr);
            public void Dispose()
            {
                Free(TexturePtr);
            }
            private static TextureEntry GetSprite(string filename)
            {
                var rect = new IntRect();
                var entry = new TextureEntry()
                {
                    HashCode = filename.GetHashCode(),
                    TexturePtr = SFMLTexture.sfTexture_createFromFile(filename, ref rect),
                };
                entry.SheetInfo = new SpriteSheetInfo();
                return entry;
            }
            public static TextureEntry NewSprite(string filename)
            {
                var entry = GetSprite(filename);
                var size = entry.GetSizeDirectly();
                entry.SheetInfo = new SpriteSheetInfo(1, size, size);
                return entry;
            }
            public static TextureEntry NewSpriteSheet_CalcCount(string filename, Vector2u spriteSize)
            {
                var entry = GetSprite(filename);
                var size = entry.GetSizeDirectly();
                var count = (int)( ( size.X / spriteSize.X ) / ( size.Y / spriteSize.Y ) );
                entry.SheetInfo = new SpriteSheetInfo(count, size, spriteSize);
                return entry;
            }
            public static TextureEntry NewSpriteSheet(string filename, int count, Vector2u spriteSize)
            {
                var entry = GetSprite(filename);
                entry.SheetInfo = new SpriteSheetInfo(count, entry.GetSizeDirectly(), spriteSize);
                return entry;
            }
        }
        /*
        
        internal struct TextureEntry : IDisposable
        {
            public static readonly TextureEntry Null = new TextureEntry();
            public int HashCode;
            public SpriteInfo Info;
            public IntPtr TexturePtr;
            private Vector2u GetSizeDirectly() => SFMLTexture.sfTexture_getSize(TexturePtr);
            public void Dispose()
            {
                Free(TexturePtr);
            }
            private static TextureEntry GetSprite(string filename)
            {
                var rect = new IntRect();
                var entry = new TextureEntry()
                {
                    HashCode = filename.GetHashCode(),
                    TexturePtr = SFMLTexture.sfTexture_createFromFile(filename, ref rect),
                };
                entry.Info = SpriteInfo.NormalSprite(entry.GetSizeDirectly());
                return entry;
            }
            public static TextureEntry NewSprite(string filename)
            {
                var entry = GetSprite(filename);
                entry.Info = SpriteInfo.NormalSprite(entry.GetSizeDirectly());
                return entry;
            }
            public static TextureEntry NewSpriteSheet(string filename, Vector2u spriteSize)
            {
                var entry = GetSprite(filename);
                entry.Info = SpriteInfo.SpriteSheet(entry.GetSizeDirectly(), spriteSize);
                return entry;
            }
            public static TextureEntry NewSpriteFromExistingSheet(TextureEntry* spriteSheet, int spriteSheetIndex)
            {
                var entry = new TextureEntry()
                {
                    HashCode = spriteSheet->HashCode + spriteSheetIndex + 1,
                    TexturePtr = spriteSheet->TexturePtr,
                };
                entry.Info = SpriteInfo.SpriteFromSheet(spriteSheetIndex, spriteSheet->Info.IndividualSpriteSize);
                return entry;
            }
        }
        */
        public struct Objects
        {
            internal static void AddObjectData<T>(CObject cObject, T data) where T : unmanaged
            {
                var table = LookupTable.GetDataTable<T>();
                var index = table->AddNew(data);
                cObject.Meta->Insert(LookupTable.GetDataTableIndex<T>(), index, (IntPtr)table->GetRef<T>(index));
            }
            internal static CObject AddNewObject(long index)
            {
                var obj = CObject.New(index);
                ObjectTablePtr->AddNew(obj);
                return obj;
            }
            internal static void RemoveObjectAtIndex(int index)
            {
                ObjectTablePtr->RemoveAt<CObject>(index);
            }
            public static Dictionary<CObject, List<CObject>> VerifiedCollisionsThisFrame { get; private set; } = new Dictionary<CObject, List<CObject>>();
            public static Dictionary<CObject, List<CObject>> PossibleCollisionsThisFrame { get; private set; } = new Dictionary<CObject, List<CObject>>();

            private readonly static List<int> ignoredCollisions = new List<int>();

            public static CObject Get(string name) => GetRef(name).VolatileRead();
            public static ByRefObject GetRef(string name) => GetRef((in CString _string) => _string.Equals(name));
            public static T GetData<T>(string name) where T : unmanaged => GetDataRef<T>(name).VolatileRead();
            public static ByRefData<T> GetDataRef<T>(string name) where T : unmanaged => GetDataRef<CString, T>((in CString _string) => _string.Equals(name));
            internal static ByRefObject GetRef<T>(PredicateIn<T> predicate) where T : unmanaged
            {
                var index = LookupTable.GetDataTableIndex<T>();
                var tableCount = LookupTable.GetDataTable<T>()->TotalHookCount();
                var iterator = HookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                var next = iterator.Next;
                while(next != null)
                {
                    if(predicate(in *next->GetDataPointer<T>()))
                    {
                        return new ByRefObject(next);
                    }
                    next = iterator.Next;
                }
                return ByRefObject.Null;
            }
            internal static ByRefObject GetRef<T, U>(PredicateIn<T> predicate) where T : unmanaged where U : unmanaged
            {
                var index = LookupTable.GetDataTableIndex<T>();
                var index2 = LookupTable.GetDataTableIndex<U>();

                var tableCount = LookupTable.GetDataTableFromIndex(index)->TotalHookCount();
                var tableCount2 = LookupTable.GetDataTableFromIndex(index2)->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

                var iterator = HookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                var next = iterator.Next;
                while (next != null)
                {
                    if (predicate(in *next->GetDataPointer<T>()))
                    {
                        return new ByRefObject(next);
                    }
                    next = iterator.Next;
                }
                return ByRefObject.Null;
            }

            internal static ByRefData<T> GetDataRef<T>(PredicateIn<T> predicate) where T : unmanaged => new ByRefData<T>(GetRef(predicate).VolatilePtr());
            internal static ByRefData<U> GetDataRef<T, U>(PredicateIn<T> predicate) where T : unmanaged where U : unmanaged => new ByRefData<U>(GetRef<T, U>(predicate).VolatilePtr());

            public static void Iterate<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                var index = LookupTable.GetDataTableIndex<T>();
                var tableCount = LookupTable.GetDataTable<T>()->TotalHookCount();

                if(multi_threaded)
                {
                    var iterator = ParallelHookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>());
                    });
                }
                else
                {
                    var iterator = HookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                    var next = iterator.Next;
                    while (next != null)
                    {
                        action(ref *next->GetDataPointer<T>());
                        next = iterator.Next;
                    }
                }
            }
            public static void Iterate<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                var index = LookupTable.GetDataTableIndex<T>();
                var index2 = LookupTable.GetDataTableIndex<U>();

                var tableCount = LookupTable.GetDataTableFromIndex(index)->TotalHookCount();
                var tableCount2 = LookupTable.GetDataTableFromIndex(index2)->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

                if(multi_threaded)
                {
                    var iterator = ParallelHookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>(),
                               ref *x.GetDataPointer<U>());
                    });
                }
                else
                {
                    var iterator = HookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                    var next = iterator.Next;
                    while (next != null)
                    {
                        action(ref *next->GetDataPointer<T>(),
                               ref *next->GetDataPointer<U>());
                        next = iterator.Next;
                    }
                }
            }
            public static void Iterate<T, U, V>(RefRefRef<T, U, V> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged where V : unmanaged
            {
                var index = LookupTable.GetDataTableIndex<T>();
                var index2 = LookupTable.GetDataTableIndex<U>();
                var index3 = LookupTable.GetDataTableIndex<V>();

                var tableCount = LookupTable.GetDataTableFromIndex(index)->TotalHookCount();
                var tableCount2 = LookupTable.GetDataTableFromIndex(index2)->TotalHookCount();
                var tableCount3 = LookupTable.GetDataTableFromIndex(index3)->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);
                size = Math.Min(size, tableCount3);

                if (multi_threaded)
                {
                    var iterator = ParallelHookIterator<CObject>.GetIterator(size, x => x.HasDataOfAll(index, index2, index3));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>(),
                               ref *x.GetDataPointer<U>(),
                               ref *x.GetDataPointer<V>());
                    });
                }
                else
                {
                    var iterator = HookIterator<CObject>.GetIterator(size, x => x.HasDataOfAll(index, index2, index3));
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

            public static void IterateWithObject<T, U>(RefWithObject<T, U, CObject> action, bool mutli_threaded = false) where T : unmanaged where U : unmanaged
            {
                var index = LookupTable.GetDataTableIndex<T>();
                var index2 = LookupTable.GetDataTableIndex<U>();

                var tableCount = LookupTable.GetDataTable<T>()->TotalHookCount();
                var tableCount2 = LookupTable.GetDataTable<U>()->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

                if(mutli_threaded)
                {
                    var iterator = ParallelHookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                    iterator.Start(x =>
                    {
                        action(ref *x.GetDataPointer<T>(),
                               ref *x.GetDataPointer<U>(),
                               ref x);
                    });
                }
                else
                {
                    var iterator = HookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
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
            public static void IgnoreCollisions<T>() where T : unmanaged => ignoredCollisions.Add(LookupTable.GetDataTableIndex<T>());
            private struct CollisionCache
            {
                public bool IsSet;
                public int TransformIndex;
                public Hook* TransformHook;
                public int ColliderIndex;
                public Hook* ColliderHook;
                public Predicate<CObject> UniversalCheck;
            }
            private static CollisionCache CCache = new CollisionCache();
            public static void PossibleCollisionQuery()
            {
                PossibleCollisionsThisFrame.Clear();

                if(!CCache.IsSet)
                {
                    CCache.IsSet = true;
                    CCache.TransformIndex = LookupTable.GetDataTableIndex<Transform>();
                    CCache.TransformHook = LookupTable.GetDataTableFromIndex(CCache.TransformIndex);
                    CCache.ColliderIndex = LookupTable.GetDataTableIndex<Collider>();
                    CCache.ColliderHook = LookupTable.GetDataTableFromIndex(CCache.ColliderIndex);
                    CCache.UniversalCheck = x => x.HasDataOf(CCache.TransformIndex) && x.HasDataOf(CCache.ColliderIndex);
                }

                /*
                var index = LookupTable.GetDataTableIndex<Transform>();
                var index2 = LookupTable.GetDataTableIndex<Collider>();

                var tableCount = LookupTable.GetDataTableFromIndex(index)->TotalHookCount();
                var tableCount2 = LookupTable.GetDataTableFromIndex(index2)->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

                var iterator = HookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
                */
                var tableCount = 0;
                var tableCount2 = 0;
                fixed (Hook** hookPtr = &CCache.TransformHook)
                {
                     tableCount = (*hookPtr)->TotalHookCount();
                }
                fixed(Hook** hookPtr = &CCache.ColliderHook)
                {
                    tableCount2 = ( *hookPtr )->TotalHookCount();
                }
                var size = Math.Min(tableCount, tableCount2);
                var iterator = HookIterator<CObject>.GetIterator(size, CCache.UniversalCheck);
                var next = iterator.Next;
                while (next != null)
                {
                    var collider = next->GetDataPointer<Collider>();
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
                // Return collisions
                var originalSize = size;

                for(int i = ignoredCollisions.Count == 0 ? -1 : 0; i < ignoredCollisions.Count; i++)
                {
                    Predicate<CObject> check;

                    if (i != -1)
                    {
                        size = originalSize - LookupTable.GetDataTableFromIndex(ignoredCollisions[i])->TotalHookCount();
                        check = x => x.HasDataOf(CCache.TransformIndex) && x.HasDataOf(CCache.ColliderIndex) && !x.HasDataOf(ignoredCollisions[i]);
                    }
                    else
                        check = CCache.UniversalCheck;

                    iterator = HookIterator<CObject>.GetIterator(size, check);

                    next = iterator.Next;
                    while (next != null)
                    {
                        if (Tree->QueryOverlaps(next) is List<CObject> list && list.Count > 0)
                            PossibleCollisionsThisFrame.Add(*next, list);
                        next = iterator.Next;
                    }
                }
            }
            public static void VerifyCollisions(bool physics_enabled)
            {
                if (PossibleCollisionsThisFrame.Count == 0)
                    return;
                VerifiedCollisionsThisFrame.Clear();
                var delta = CTime.DeltaTime;
                foreach(var cObject in PossibleCollisionsThisFrame.Keys)
                {
                    var list = PossibleCollisionsThisFrame[cObject];
                    var transform = cObject.GetDataPointer<Transform>();
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].IsActiveNull())
                            continue;

                        var other = list[i].GetDataPointer<Transform>();
                        if (!list[i].IsActiveNull() && transform->ExpensiveIntersection(other))
                        {
                            if (VerifiedCollisionsThisFrame.ContainsKey(cObject))
                            {
                                VerifiedCollisionsThisFrame[cObject].Add(list[i]);
                            }
                            else
                            {
                                VerifiedCollisionsThisFrame.Add(cObject, new List<CObject> { list[i] });
                            }

                            if (physics_enabled)
                            {
                                throw new NotImplementedException();

                                // Get physics body and velocity
                                
                                /*if (list[i].ID == 6) // test condition, objects are "applied force to" twice leading to 0'd velocities
                                {
                                    continue;
                                }*/

                                //var thisBody = cObject.GetDataPointer<PhysicsBody>();
                                //var otherBody = list[i].GetDataPointer<PhysicsBody>();
                            }
                        }
                    }
                }
            }
        }
        internal struct Textures
        {
            public static IntPtr TryAddTexture(string filename)
            {
                var ptr = FindTexture(filename);
                if(ptr == IntPtr.Zero)
                    ptr = NewTexture(filename);
                return ptr;
            }
            public static IntPtr FindTexture(string filename)
            {
                return FindTexture(filename.GetHashCode());
            }
            public static IntPtr FindTexture(int hashCode)
            {
                var entry = TextureTablePtr->FindRef((in TextureEntry x) => x.HashCode == hashCode);
                if (entry == null)
                    return IntPtr.Zero;
                return (IntPtr)entry;
            }
            public static IntPtr NewTexture(string filename)
            {
                var index = TextureTablePtr->AddNew(TextureEntry.NewSprite(filename));
                return (IntPtr)TextureTablePtr->GetRef<TextureEntry>(index);
            }
            public static IntPtr NewSpriteSheet(string filename, int count, Vector2u individualSpriteSize)
            {
                var index = TextureTablePtr->AddNew(TextureEntry.NewSpriteSheet(filename, count, individualSpriteSize));
                return (IntPtr)TextureTablePtr->GetRef<TextureEntry>(index);
            }
            public static IntPtr NewSpriteSheet_CalcCount(string filename, Vector2u individualSpriteSize)
            {
                var index = TextureTablePtr->AddNew(TextureEntry.NewSpriteSheet_CalcCount(filename, individualSpriteSize));
                return (IntPtr)TextureTablePtr->GetRef<TextureEntry>(index);
            }
            public static IntRect GetSpriteRectFromSheet(string filename, int index)
            {
                var entry = FindTexture(filename);
                if (entry == IntPtr.Zero)
                    return new IntRect();
                return ((TextureEntry*)entry)->SheetInfo.GetTextureRectAtIndex(index);
            }
        }
        internal struct MetaData : IDisposable
        {
            public int MaxSize;
            public CTuple<int, IntPtr> DataPtr;
            public T* Get<T>(int tableIndex) where T : unmanaged
            {
                if (tableIndex < 0 || tableIndex >= MaxSize)
                    return null;
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    return (T*)( ptrPtr + tableIndex )->Item2;
                }
            }
            public void Insert(int tableIndex, int index, IntPtr ptr)
            {
                if (tableIndex < 0 || tableIndex >= MaxSize)
                    return;
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    *( ptrPtr + tableIndex ) = new CTuple<int, IntPtr>(index, ptr);
                }
            }
            public void Remove(int tableIndex)
            {
                if (tableIndex < 0 || tableIndex >= MaxSize)
                    return;
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    *( ptrPtr + tableIndex ) = CTuple<int, IntPtr>.Null;
                }
            }
            public bool HasDataOf(int tableIndex)
            {
                if (tableIndex < 0 || tableIndex >= MaxSize)
                    return false;
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    return ( ptrPtr + tableIndex )->Item2 != IntPtr.Zero;
                }
            }
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
            public static IntPtr New()
            {
                var size = TablePtr->Entries + 1;
                var bytes = sizeof(int) + ( sizeof(CTuple<int, IntPtr>) * size );
                var ptr = Malloc(bytes);
                MemSet(ptr, 0, bytes);
                var metaSlot = new MetaData()
                {
                    MaxSize = size
                };
                *(MetaData*)ptr = metaSlot;
                return ptr;
            }
            public void Dispose()
            {
                //throw new NotImplementedException();
                fixed (CTuple<int, IntPtr>* ptrPtr = &DataPtr)
                {
                    for(int i = DataTableStartIndex; i < this.MaxSize - 1; i++)
                    {
                        var tuple = *( ptrPtr + i );
                        if(tuple.Item2 != IntPtr.Zero)
                        {
                            var table = LookupTable.GetDataTableFromIndex(i);
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
        private class Multi<Args> where Args : EventArgs // might throw error on multi threads due to inputs and checks being on different threads
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
    public abstract class Engine
    {
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

            public override void Close()
            {
                base.Close();
#if DEBUG
                Logger.Logger.Instance.WriteAllLogs();
#endif
            }

            private void OnQuit(object sender, EventArgs args)
            {
                this.Close();
            }

            private void OnKeyPress(object sender, KeyEventArgs args)
            {
                Input.SetKeyPressedArgs(args);
            }

            private void OnKeyRelease(object sender, KeyEventArgs args)
            {
                Input.SetKeyReleasedArgs(args);
            }

            private void OnMousePress(object sender, MouseButtonEventArgs args)
            {
                Input.SetMouseButtonPressedArgs(args);
            }
            private void OnMouseRelease(object sender, MouseButtonEventArgs args)
            {
                Input.SetMouseButtonReleasedArgs(args);
            }
        }

        internal static Engine MainEngine = null;
        internal static EngineWindow MainWindow => MainEngine?.ThisWindow;


        internal EngineWindow ThisWindow = null;

        public abstract void Initialise();
        public virtual EngineSettings Settings => new EngineSettings(4, 1024, 1024, 10, new Vector2u(800, 600), "Window", false, false, false);
        private void GameLoop() // might make "public virtual"
        {
            var delta = CTime.DeltaTime;
            for (int i = 0; i < Collection.Subsystems.Count; i++)
            {
                var subsystem = Collection.Subsystems[i];
                if (subsystem.IsStarted && subsystem.IsEnabled)
                {
#if DEBUG
                    var time = DateTime.Now;
                    subsystem.Update(delta);
                    var now = DateTime.Now;
                    Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, subsystem.Name, ( now - time ).TotalMilliseconds.ToString());
#else
                    subsystem.Update(delta);
#endif
                }
            }
            //Thread.Sleep((int)( CTime.DeltaTime * 1000 ));aa
#if SEPARATE_RENDER_THREAD
            CTime.RegisterThreadContinuance(Thread.CurrentThread);
            while (!CTime.AreOtherThreadsStillGoing(Thread.CurrentThread))
                ;
#endif
        }
        protected virtual void StartSubsystems()
        {
            foreach (var item in Collection.Subsystems)
                if (!item.IsStarted && item.IsEnabled)
                    item.Startup();
        }
        public static void Stop()
        {
            if (MainEngine != null)
            {
                MainWindow.Close();
            }
        }
        public static void Start(Type engineType)
        {
            if (engineType.IsSubclassOf(typeof(Engine)))
            {
#if SEPARATE_RENDER_THREAD
                CTime.SetGameLoopThread(Thread.CurrentThread);
#endif
#if DEBUG
                Logger.Logger.CreateLogger("TempLog_" + DateTime.Now.Ticks.ToString());
#endif

                MainEngine = (Engine)Activator.CreateInstance(engineType);

                Entry(MainEngine.Settings.MainTableSize, MainEngine.Settings.ObjectTableSize, MainEngine.Settings.DataTableSize, MainEngine.Settings.TextureTableSize);

                Collection.AddNewSubsystem<RenderSubsystem>();
                Collection.AddNewSubsystem<UISubsystem>();
                Collection.Subsystems[0].Startup(); // special case exception
                Collection.Subsystems[1].Startup(); // special case exception

                if (MainEngine.Settings.EnablePhysics)
                {
                    Subsystem.AddNewDataType<PhysicsBody>();
                    Collection.AddNewSubsystem<PhysicsSubsystem>();
                }
                if (MainEngine.Settings.EnableCollisions)
                    Subsystem.AddNewDataType<Collider>();
                if(MainEngine.Settings.EnableAnimations)
                    Subsystem.AddNewDataType<Animation>();

                MainEngine.Initialise();

                Collection.SubsystemsToTestFor(new KeyValuePair<Type, bool>(typeof(CollisionSubsystem), MainEngine.Settings.EnableCollisions),
                                               new KeyValuePair<Type, bool>(typeof(AnimationSubsystem), MainEngine.Settings.EnableAnimations));

                Subsystem.SystemFlags.SetFlags();

                while (MainEngine.ThisWindow is null && CTime.DeltaTime == 0f)
                    ;

                while (MainWindow.IsOpen)
                {
                    var gltime = DateTime.Now;
                    MainEngine.GameLoop();
                    var glnow = DateTime.Now;
                    float mpf = (float)( glnow - gltime ).TotalMilliseconds;
                    CTime.FPS = 1000f / mpf;
#if DEBUG
                    Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, "GameLoop", mpf.ToString());
#endif
                }
            }
        }
    }
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
    public static class Collection
    {
        internal static readonly List<Subsystem> Subsystems = new List<Subsystem>();
        internal static void AddNewSubsystem<System>() where System : Subsystem
        {
            var type = typeof(System);
            if (!type.IsAbstract && !Subsystems.Any(x => x.GetType() == type))
            {
                Subsystems.Add((Subsystem)Activator.CreateInstance(type));
            }
        }
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
    }
    public abstract class Subsystem
    {
        internal static readonly object SyncRoot = new object();
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
        //public static RefObjectTable Entities => UnmanagedCSharp.Entities;
        public static void AddNewDataType<T>() where T : unmanaged
        {
            if (SystemFlags.AddNewDataTypes) // throw on fail
                LookupTable.AddNewDataType<T>();
        }
        public static void AddNewSubsystem<System>() where System : Subsystem
        {
            if (SystemFlags.AddNewSubsystems) // throw on fail
                Collection.AddNewSubsystem<System>();
        }

        public Subsystem()
        {
            Name = GetType().Name;
        }
        public readonly string Name;
        public bool IsEnabled { get; set; } = true;
        public bool IsStarted { get; private set; } = false;
        public abstract void Update(float deltaSeconds);
        public virtual void Startup() { IsStarted = true; }
    }

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

            CTime.SetRenderThread(Thread.CurrentThread);

            while (Engine.MainWindow.IsOpen)
            {
                Engine.MainWindow.DispatchEvents();
                var time = DateTime.Now;
                lock (SyncRoot)
                {
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
                }
                Engine.MainWindow.Display();
                Engine.MainWindow.Clear();
                CTime.DeltaTime = (float)( ( DateTime.Now - time ).TotalSeconds );
#if DEBUG
                Logger.Logger.Instance.AddLog(LogKey.StatisticsLog, "Render", ( CTime.DeltaTime * 1000 ).ToString());
#endif
                CTime.RegisterThreadContinuance(Thread.CurrentThread);
                while (!CTime.AreOtherThreadsStillGoing(Thread.CurrentThread))
                    ;
            }
        }
#endif
    }
    /*
    public sealed class BoundarySubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Transform transform) =>
            {
                var window = Engine.MainEngine.Settings.WindowDimensions;
                var current_x = transform.Position.X;
                var current_y = transform.Position.Y;
                if (current_x < 0)
                    current_x = 0;
                else if (current_x > window.X)
                    current_x = window.X;

                if (current_y < 0)
                    current_y = 0;
                else if (current_y > window.Y)
                    current_y = window.Y;

                transform.Position = new Vector2f(current_x, current_y);
            });
        }
    }
    */
    public class PhysicsSubsystem : Subsystem
    {
        private Vector2u Window = Engine.MainEngine.Settings.WindowDimensions;
        public override void Update(float deltaSeconds)
        {
            Objects.Iterate((ref PhysicsBody body, ref Transform transform) =>
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

    public abstract class CollisionSubsystem : Subsystem
    {
        public virtual bool VerifyCollisions { get; protected set; } = false;
        public static Dictionary<CObject, List<CObject>> GetPossibleCollisions => Objects.PossibleCollisionsThisFrame;
        public static Dictionary<CObject, List<CObject>> GetVerifiedCollisions => Objects.VerifiedCollisionsThisFrame;
        public override void Update(float deltaSeconds)
        {
            Objects.PossibleCollisionQuery();
            if(VerifyCollisions)
            {
                Objects.VerifyCollisions(Engine.MainEngine.Settings.EnablePhysics);
            }
        }
    }

    public abstract class AnimationSubsystem : Subsystem
    {
        
    }

    public class UISubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Objects.Iterate((ref Button button, ref Transform transform) =>
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
    public unsafe struct CString : IComponentData, IDisposable, IEquatable<string>
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
}
namespace ECS.Graphics
{
    using static RenderWindow;
    using static UnmanagedCSharp;
    using static Maths.RNG;
    public enum Anchor
    {
        TOP_LEFT = 0,
        TOP_RIGHT = 1,
        BOTTOM_LEFT = 2,
        BOTTOM_RIGHT = 3,
        CENTRE = 4,
    }
    public static class GraphicsExtensions
    {
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
        public static Vector2f GetRelativePositionToAnchor(this Anchor anchor, Vector2f position)
        {
            return anchor.GetAbsoluteFromAnchor() + position;
        }
    }
    public struct Transform : IComponentData
    {
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
                //myBounds = new FloatRect(new Vector2f(), (Vector2f)value);
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
        private FloatRect GetGlobalBounds()
        {
            return SFMLTransform.TransformRect(GetLocalBounds());
        }
        private FloatRect GetLocalBounds()
        {
            return new FloatRect(0, 0, mySize.X, mySize.Y);
        }
        public bool ExpensiveIntersection(Transform other)
        {
            return GetGlobalBounds().Intersects(other.GetGlobalBounds());
        }
        public Vector2f GetAnchoredPosition()
        {
            return myAnchor.GetRelativePositionToAnchor(myPosition);
        }
        internal unsafe bool ExpensiveIntersection(Transform* other)
        {
            return GetGlobalBounds().Intersects(other->GetGlobalBounds());
        }
        public Transform(float x, float y, uint height, uint width, float origin_x, float origin_y, float rotation, Anchor anchor) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(origin_x, origin_y), rotation, anchor) { }
        public Transform(float x, float y, uint height, uint width, float origin_x, float origin_y, float rotation) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(origin_x, origin_y), rotation, Anchor.TOP_LEFT) { }
        public Transform(float x, float y, uint height, uint width, float origin_x, float origin_y) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(origin_x, origin_y), 0, Anchor.TOP_LEFT) { }
        public Transform(float x, float y, uint height, uint width) : this(new Vector2f(x, y), new Vector2u(height, width), new Vector2f(height, width) / 2, 0, Anchor.TOP_LEFT) { }
        public Transform(Vector2f position, Vector2u size, Vector2f origin, float rotation, Anchor anchor)
        {
            mySize = size;
            //myBounds = new FloatRect(new Vector2f(), (Vector2f)size);
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
        private Vector2f myOrigin;// = new Vector2f(0, 0);
        private Vector2f myPosition;// = new Vector2f(0, 0);
        private float myRotation;// = 0;
        private Vector2f myScale;// = new Vector2f(1, 1);
        private SFMLTransform myTransform;
        private SFMLTransform myInverseTransform;
        private bool myTransformNeedUpdate;// = true;
        private bool myInverseNeedUpdate;// = true;
        private Anchor myAnchor;
    }
    public unsafe struct Texture : Drawable, IComponentData
    {
        public static readonly Texture Null = new Texture();

        internal int Index;
        internal bool ShouldDraw;
        internal Vertex4 Vertices;
        internal IntPtr TexturePtr;
        internal TextureEntry* Entry => (TextureEntry*)TexturePtr;
        public Texture(string filename)
        {
            TexturePtr = Textures.TryAddTexture(filename);
            Vertices = new Vertex4();
            ShouldDraw = true;
            Index = -1;
            UpdateTexture(TexturePtr);
        }
        public static Texture CreateSpriteSheet(string filename, Vector2u spriteSize)
        {
            var texture = new Texture()
            {
                TexturePtr = Textures.NewSpriteSheet_CalcCount(filename, spriteSize),
                Vertices = new Vertex4(),
                ShouldDraw = true,
            };
            texture.UpdateTexture(texture.TexturePtr);
            return texture;
        }
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
        public void UpdateTexture(IntPtr texture, int index = 0)
        {
            if (texture != IntPtr.Zero)
            {
                this.Index = index;
                this.TexturePtr = texture;
                var size = Entry->SheetInfo.IndividualSpriteSize;//Info.SpriteSize;
                var rect = Entry->SheetInfo.GetTextureRectAtIndex(index);
                var right = rect.Left + rect.Width;
                var bottom = rect.Top + rect.Height;
                Vertices.Vertex0 = new Vertex(new Vector2f(0, 0), Color.White, new Vector2f(rect.Left, rect.Top));
                Vertices.Vertex1 = new Vertex(new Vector2f(0, size.Y), Color.White, new Vector2f(rect.Left, bottom));
                Vertices.Vertex2 = new Vertex(new Vector2f(size.X, 0), Color.White, new Vector2f(right, rect.Top));
                Vertices.Vertex3 = new Vertex(new Vector2f(size.X, size.Y), Color.White, new Vector2f(right, bottom));
            }
        }
        public Texture GetSpriteFromSheet(int index)
        {
            var _this = this;
            _this.UpdateTexture(TexturePtr, index);
            return _this;
        }
        public Texture CalculateShouldDraw(AABB _this, AABB other)
        {
            var texture = this;
            texture.ShouldDraw = other.Overlaps(_this);
            return texture;
        }
        public void SetColor(Color color)
        {
            Vertices.Vertex0.Color = color;
            Vertices.Vertex1.Color = color;
            Vertices.Vertex2.Color = color;
            Vertices.Vertex3.Color = color;
        }
        public Texture SetModifiedTextureColor(Color color)
        {
            this.SetColor(color);
            return this;
        }
        public Vector2u GetSize => Entry->SheetInfo.IndividualSpriteSize;
        public Color GetColor()
        {
            return Vertices.Vertex0.Color;
        }
        public void Draw(RenderTarget target, RenderStates states)
        {
#if SEPARATE_RENDER_THREAD
            if (Thread.CurrentThread != RenderSubsystem.RenderThread)
                throw new RenderThreadCrossDrawingException();
#endif
            if (!ShouldDraw)
                return;
            states.CTexture = Entry->TexturePtr;
            ( (RenderWindow)target ).Draw(Vertices, PrimitiveType.TriangleStrip, states);
        }
    }
}
namespace ECS.Maths
{
    public static class Constants
    {
        public static readonly Vector2f Gravity = new Vector2f(0, 9.80665f);
        public static Vector2f ModifiableGravity = new Vector2f(0, 0);
    }

    public static class RNG
    {
        private static readonly Random Random = new Random();
        public static int Next(int min, int max) => Random.Next(min, max);
    }

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
        public static Vector2f Direction(this Vector2f this_vector, Vector2f target)
        {
            return (target - this_vector).Normalise();
        }
        public static Vector2f Min(this FloatRect box) => new Vector2f(Math.Min(box.Left, box.Left + box.Width), Math.Min(box.Top, box.Top + box.Height));
        public static Vector2f Max(this FloatRect box) => new Vector2f(Math.Max(box.Left, box.Left + box.Width), Math.Max(box.Top, box.Top + box.Height));

        public static float ToRotation(this Vector2f direction) => ( (float)( Math.Atan2(direction.Y, direction.X) / ( 2 * Math.PI ) ) * 360f ) + 90f; // specific to SFML as 0, 0 is top-left

        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }

}
namespace ECS.Collections
{
    using static CAllocation;
    public static unsafe class Generic
    {
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
            public LocalArray(int entries, int size) : this(size)
            {
                Entries = entries;
            }
            private void ReinforceSize(int index)
            {
                if (index < 0 || index >= MaxSize)
                    throw new IndexInvalidException("a local array", index, 0, MaxSize);
            }
            public T this[int index]
            {
                get => ReadAt(index);
                set => ModifyAt(index, value);
            }
            public void Add(T item)
            {
                ModifyAt(Entries++, item);
            }
            public T* ReadPointerAt(int index)
            {
                ReinforceSize(index);
                fixed (IntPtr* ptr = &Items)
                {
                    return ( ( (T*)*ptr ) + index );
                }
            }
            private T ReadAt(int index)
            {
                ReinforceSize(index);
                fixed (IntPtr* ptr = &Items)
                {
                    return *( ( (T*)*ptr ) + index );
                }
            }
            public void ModifyAt(int index, T new_data)
            {
                ReinforceSize(index);
                fixed (IntPtr* ptr = &Items)
                {
                    *( (T*)*ptr + index ) = new_data;
                }
            }
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
            public void Resize(int size)
            {
                Items = CGC.ResizeEntireBlock(Items, sizeOf, MaxSize, size);
                MaxSize = size;
                AllocatedBytes = sizeOf * size;
            }
            public void Clear()
            {
                Entries = 0;
                MemSet(Items, 0, AllocatedBytes);
            }
            public void Dispose()
            {
                Free(Items);
            }
        }
        // Need to optimise dictionary lookup
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
            public LocalDictionary(int capacity)
            {
                entries = new LocalArray<Entry>(capacity);
            }
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
            public void Add(T key, U value)
            {
                if (ContainsKey(key) == -1)
                {
                    entries.Add(new Entry(key, value));
                }
            }
            public void Remove(T key)
            {
                if (ContainsKey(key) is int _int && _int != -1)
                {
                    entries.RemoveAt(_int);
                }
            }
            public void Resize(int size) => entries.Resize(size);
            public void Dispose()
            {
                entries.Dispose();
            }
        }
    }
}
namespace ECS.Physics
{
    public static class Physics
    {
        public static void ApplyForce(ref this PhysicsBody body, Vector2f direction)
        {
            body.Velocity += direction;
        }
    }
    public struct PhysicsBody : IComponentData
    {
        public static readonly Vector2f ConstantAcceleration = new Vector2f(1, 1);

        public Vector2f Velocity;
        public Vector2f Acceleration;
    }
    public enum ColliderType
    {
        Square = 0,
    }
    public struct Collider : IComponentData
    {
        public bool IsInTree;
        public ColliderType Type;
    }
}
namespace ECS.Collision
{
    using static Maths.Maths;
    using static Collections.Generic;
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

        public bool Overlaps(AABB other)
        {
            return maxX > other.minX &&
            minX < other.maxX &&
            maxY > other.minY &&
            minY < other.maxY;
        }

        public bool Contains(AABB other)
        {
            return other.minX >= minX &&
            other.maxX <= maxX &&
            other.minY >= minY &&
            other.maxY <= maxY;
        }

        public bool Contains(Vector2f point)
        {
            return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
        }

        public AABB Merge(AABB other)
        {
            return new AABB(
            Min(minX, other.minX), Min(minY, other.minY),
            Max(maxX, other.maxX), Max(maxY, other.maxY));
        }

        public AABB Intersection(AABB other)
        {
            return new AABB(
            Max(minX, other.minX), Max(minY, other.minY),
            Min(maxX, other.maxX), Min(maxY, other.maxY));
        }

        public float GetWidth() => ( maxX - minX );
        public float GetHeight() => ( maxY - minY );

    }
    public unsafe struct AABBNode 
    {
        public static readonly int NULL_NODE = int.MaxValue;

        public AABB AssociatedAABB;
        public CObject* ObjectPointer;
        public int ParentNodeIndex;
        public int LeftNodeIndex;
        public int RightNodeIndex;
        public int NextNodeIndex;
        public bool IsLeaf() => LeftNodeIndex == NULL_NODE;
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
    // # https://www.azurefromthetrenches.com/introductory-guide-to-aabb-tree-collision-detection/
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
        private int allocateNode()
        {
            
            if (nextFreeNodeIndex == AABBNode.NULL_NODE)
            {
                //assert(_allocatedNodeCount == _nodeCapacity);

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
        private void deallocateNode(int nodeIndex)
        {
            AABBNode* deallocatedNode = nodes.ReadPointerAt(nodeIndex);
            deallocatedNode->NextNodeIndex = nextFreeNodeIndex;
            nextFreeNodeIndex = nodeIndex;
            allocatedNodeCount--;
        }

        private void insertLeaf(int leafNodeIndex)
        {
            // make sure we're inserting a new leaf
            //assert(_nodes[leafNodeIndex].parentNodeIndex == AABB_NULL_NODE);
            //assert(_nodes[leafNodeIndex].leftNodeIndex == AABB_NULL_NODE);
            //assert(_nodes[leafNodeIndex].rightNodeIndex == AABB_NULL_NODE);

            // if the tree is empty then we make the root the leaf
            if (rootNodeIndex == AABBNode.NULL_NODE)
            {
                rootNodeIndex = leafNodeIndex;
                return;
            }

            // search for the best place to put the new leaf in the tree
            // we use surface area and depth as search heuristics
            int treeNodeIndex = rootNodeIndex;
            AABBNode* leafNode = nodes.ReadPointerAt(leafNodeIndex);
            while (!nodes.ReadPointerAt(treeNodeIndex)->IsLeaf())
            {
                // because of the test in the while loop above we know we are never a leaf inside it
                AABBNode* treeNode = nodes.ReadPointerAt(treeNodeIndex);
                int leftNodeIndex = treeNode->LeftNodeIndex;
                int rightNodeIndex = treeNode->RightNodeIndex;
                AABBNode* leftNode = nodes.ReadPointerAt(leftNodeIndex);
                AABBNode* rightNode = nodes.ReadPointerAt(rightNodeIndex);

                AABB combinedAabb = treeNode->AssociatedAABB.Merge(leafNode->AssociatedAABB);

                float newParentNodeCost = 2.0f * combinedAabb.SurfaceArea;
                float minimumPushDownCost = 2.0f * ( combinedAabb.SurfaceArea - treeNode->AssociatedAABB.SurfaceArea );

                // use the costs to figure out whether to create a new parent here or descend
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

                // if the cost of creating a new parent node here is less than descending in either direction then
                // we know we need to create a new parent node, errrr, here and attach the leaf to that
                if (newParentNodeCost < costLeft && newParentNodeCost < costRight)
                {
                    break;
                }

                // otherwise descend in the cheapest direction
                if (costLeft < costRight)
                {
                    treeNodeIndex = leftNodeIndex;
                }
                else
                {
                    treeNodeIndex = rightNodeIndex;
                }
            }

            // the leafs sibling is going to be the node we found above and we are going to create a new
            // parent node and attach the leaf and this item
            int leafSiblingIndex = treeNodeIndex;
            AABBNode* leafSibling = nodes.ReadPointerAt(leafSiblingIndex);
            int oldParentIndex = leafSibling->ParentNodeIndex;
            int newParentIndex = allocateNode();
            AABBNode* newParent = nodes.ReadPointerAt(newParentIndex);
            newParent->ParentNodeIndex = oldParentIndex;
            newParent->AssociatedAABB = leafNode->AssociatedAABB.Merge(leafSibling->AssociatedAABB); // the new parents aabb is the leaf aabb combined with it's siblings aabb
            newParent->LeftNodeIndex = leafSiblingIndex;
            newParent->RightNodeIndex = leafNodeIndex;
            leafNode->ParentNodeIndex = newParentIndex;
            leafSibling->ParentNodeIndex = newParentIndex;

            if (oldParentIndex == AABBNode.NULL_NODE)
            {
                // the old parent was the root and so this is now the root
                rootNodeIndex = newParentIndex;
            }
            else
            {
                // the old parent was not the root and so we need to patch the left or right index to
                // point to the new node
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

            // finally we need to walk back up the tree fixing heights and areas
            treeNodeIndex = leafNode->ParentNodeIndex;
            fixUpwardsTree(treeNodeIndex);
        }


        private void removeLeaf(int leafNodeIndex)
        {
            // if the leaf is the root then we can just clear the root pointer and return
            if (leafNodeIndex == rootNodeIndex)
            {
                rootNodeIndex = AABBNode.NULL_NODE;
                return;
            }

            AABBNode* leafNode = nodes.ReadPointerAt(leafNodeIndex);
            if(leafNode->ParentNodeIndex == int.MaxValue) // fail safe
                return;
            int parentNodeIndex = leafNode->ParentNodeIndex;
            AABBNode* parentNode = nodes.ReadPointerAt(parentNodeIndex);
            int grandParentNodeIndex = parentNode->ParentNodeIndex;
            int siblingNodeIndex = parentNode->LeftNodeIndex == leafNodeIndex ? parentNode->RightNodeIndex : parentNode->LeftNodeIndex;
            //assert(siblingNodeIndex != AABB_NULL_NODE); // we must have a sibling
            AABBNode* siblingNode = nodes.ReadPointerAt(siblingNodeIndex);

            if (grandParentNodeIndex != AABBNode.NULL_NODE)
            {
                // if we have a grand parent (i.e. the parent is not the root) then destroy the parent and connect the sibling to the grandparent in its
                // place
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
                // if we have no grandparent then the parent is the root and so our sibling becomes the root and has it's parent removed
                rootNodeIndex = siblingNodeIndex;
                siblingNode->ParentNodeIndex = AABBNode.NULL_NODE;
                deallocateNode(parentNodeIndex);
            }

            leafNode->ParentNodeIndex = AABBNode.NULL_NODE;
        }

        private void updateLeaf(int leafNodeIndex, AABB newAaab)
        {
            AABBNode* node = nodes.ReadPointerAt(leafNodeIndex);

            // if the node contains the new aabb then we just leave things
            // TODO: when we add velocity this check should kick in as often an update will lie within the velocity fattened initial aabb
            // to support this we might need to differentiate between velocity fattened aabb and actual aabb
            if (node->AssociatedAABB.Contains(newAaab))
            { return; }

            removeLeaf(leafNodeIndex);
            node->AssociatedAABB = newAaab;
            insertLeaf(leafNodeIndex);
        }



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

        public void InsertObject(CObject* cObject)
        {
            int nodeIndex = allocateNode();
            AABBNode* node = nodes.ReadPointerAt(nodeIndex);

            node->AssociatedAABB = cObject->Meta->Get<Transform>(transformDataTableIndex)->BoundingBox;//cObject->GetDataPointer<Transform>()->BoundingBox;
            node->ObjectPointer = cObject;

            insertLeaf(nodeIndex);
            map[cObject->ID] = nodeIndex;

            //Console.WriteLine(nodeIndex);
            //Console.WriteLine(cObject->ID);
            //Console.WriteLine(map[cObject->ID]);
        }
        public void RemoveObject(CObject* cObject)
        {
            int nodeIndex = map[cObject->ID];
            removeLeaf(nodeIndex);
            deallocateNode(nodeIndex);
            map.Remove(cObject->ID);
        }

        public void UpdateObject(CObject* cObject)
        {
            int nodeIndex = map[cObject->ID];
            updateLeaf(nodeIndex, cObject->Meta->Get<Transform>(transformDataTableIndex)->BoundingBox);
        }

        public List<CObject> QueryOverlaps(CObject cObject)
        {
            List<CObject> overlaps = new List<CObject>();
            Stack<int> stack = new Stack<int>();
            AABB testAabb = cObject.Meta->Get<Transform>(transformDataTableIndex)->BoundingBox;
            stack.Push(rootNodeIndex);
            while (stack.Count != 0)
            {
                int nodeIndex = stack.Pop();

                if (nodeIndex == AABBNode.NULL_NODE)
                    continue;

                AABBNode* node = nodes.ReadPointerAt(nodeIndex);

                if (node->AssociatedAABB.Overlaps(testAabb))
                {
                    if (node->IsLeaf() && !node->ObjectPointer->Equals(cObject))
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
            return overlaps;
        }
        public List<CObject> QueryOverlaps(CObject* cObject)
        {
            return QueryOverlaps(*cObject);
        }

        public void Dispose()
        {
            for(int i = 0; i < nodes.MaxSize; i++)
            {
                if(nodes[i].ObjectPointer != null && !nodes[i].ObjectPointer->IsActiveNull())
                {
                    Collider* collider = nodes[i].ObjectPointer->GetDataPointer<Collider>();
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
    public struct Button : IComponentData
    {
        private CDelegate OnClick;
        public Button(Action action)
        {
            OnClick = CDelegate.New(action);
        }
        public void Click()
        {
            OnClick.Invoke<Action>();
        }
    }
    public struct Text : Drawable, IComponentData
    {
        private CString String;
        private uint CharacterSize;
        private Font Font;
        private Color Color;
        private LocalArray<Vertex> vertices;
        private bool needsUpdate;
        public Text(string text, Font font, uint size = 15)
        {
            String = new CString(text);
            Font = font;
            CharacterSize = size;
            Color = Color.White;
            needsUpdate = true;
            vertices = new LocalArray<Vertex>(String.Length * 6);
        }
        public void ChangeText(string new_text)
        {
            if (new_text.Length == String.Length && new_text.GetHashCode() == String.stringHash)
                return;
            String.Dispose();
            String = new CString(new_text);
            vertices = new LocalArray<Vertex>(String.Length * 6);
            needsUpdate = true;
        }
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
            //whiteSpaceWidth += letterSpacing;
            //float lineSpacing = Font.GetLineSpacing(CharacterSize);
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
    
    public struct Font
    {
        public IntPtr CFont;
        public Font(string filename)
        {
            CFont = SFML.Graphics.Font.sfFont_createFromFile(filename);
        }
        public Glyph GetGlyph(uint characterCode, uint characterSize)
        {
            return SFML.Graphics.Font.sfFont_getGlyph(CFont, characterCode, characterSize, false, 0f);
        }
        public IntPtr GetTexture(uint characterSize)
        {
            return SFML.Graphics.Font.sfFont_getTexture(CFont, characterSize);
        }
        public float GetKerning(uint previousChar, uint nextChar, uint characterSize)
        {
            return SFML.Graphics.Font.sfFont_getKerning(CFont, previousChar, nextChar, characterSize);
        }
    }
}
namespace ECS.Animations
{
    using static CAllocation;
    public unsafe struct Animation : IComponentData
    {
        private struct AnimState
        {
            public IntPtr Entry;
            public int Index;
            public AnimState(IntPtr ptr, int index)
            {
                Entry = ptr;
                Index = index;
            }
        }
        public uint FrameCount;
        public IntPtr Frames;
        public Animation(uint frameCount)
        {
            FrameCount = frameCount;
            Frames = Malloc((int)(sizeof(AnimState) * frameCount));
            MemSet(Frames, 0, (int)(sizeof(AnimState) * frameCount));
        }
        public void SetAnimationFrameTo(int index, Texture texture)
        {
            if (index < 0 || index >= FrameCount)
                throw new IndexInvalidException("an animations' frames", index, 0, (int)FrameCount);
            fixed(IntPtr* pFrames = &Frames)
            {
                var anim = (AnimState*)*pFrames;
                *( anim + index ) = new AnimState(texture.TexturePtr, index);
            }
        }
        public void SetAnimationFrameOf(int index, ref Texture texture)
        {
            if (index < 0 || index >= FrameCount)
                throw new IndexInvalidException("an animations' frames", index, 0, (int)FrameCount);
            fixed (IntPtr* pFrames = &Frames)
            {
                var anim = ((AnimState*)*pFrames) + index;
                if (texture.TexturePtr == anim->Entry && texture.Index == anim->Index)
                    return;
                texture.TexturePtr = anim->Entry;
                texture.Index = anim->Index;
                texture.UpdateRectOnly();
            }
        }
    }
}
namespace ECS.GarbageCollection
{
    using static CAllocation;
    /// <summary>
    /// C Garbage Collection
    /// </summary>
    public unsafe static class CGC
    {
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
    public abstract class CoreException : Exception
    {
        public CoreException(string message) : base(message + "\nEngine core invalidated.")
        {
            Logger.Instance.AddLog(LogKey.Error, GetType().Name, message);
            Logger.Instance.WriteAllLogs();
        }
    }
    public class IncompatibleTypeException : CoreException
    {
        public IncompatibleTypeException(string typeName, string desiredTypeName) : base(typeName + " is an incompatible type as it does not inherit " + desiredTypeName) { } 
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
        public DataPointerNullException(string typeName) : base("Object could not access " + typeName + " related data as it did not possess any. Please check the object is valid and you added the relevant data to it.") { }
        public DataPointerNullException(string typeName, string typeName2) : base("Object could not access " + typeName + " and/or " + typeName2 + " related data as it did not possess any. Please check the object is valid and you added the relevant data to it.") { }
    }
    public class TextureNotFoundException : TextureException
    {
        public TextureNotFoundException(string filename) : base("The loaded texture at " + filename + " could not be found. Either the path is wrong or the texture was not previously loaded.") { }
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

    public class Logger
    {
        public static Logger Instance { get; private set; } = null;

        private static readonly object SyncRoot = new object();
        private const string LoggerExtension = ".txt";

        private readonly List<string> Logs = new List<string>();

        public readonly bool IsFileRelative = true;
        public readonly string LogFileName = "Log";

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

        public IEnumerable<LogItem> ReadAllLogs(bool includeCurrentOnes = false)
        {
            lock (SyncRoot)
            {
                foreach (var item in File.ReadAllLines(GetFileLocation()))
                {
                    yield return LogItem.FromString(item);
                }
                if (includeCurrentOnes)
                {
                    foreach (var item in Logs)
                    {
                        yield return LogItem.FromString(item);
                    }
                }
            }
        }

        public void PrintAllLogs()
        {
            foreach (var item in ReadAllLogs())
            {
                Console.WriteLine(item.ToHumanReadable());
            }
        }

        private Logger(string filename, bool relativeFileName = true, bool deleteOld = true)
        {
            LogFileName = filename;
            IsFileRelative = relativeFileName;

            if (deleteOld && File.Exists(GetFileLocation()))
            {
                File.Delete(GetFileLocation());
            }
        }

        private void AddLog(string log)
        {
            lock (SyncRoot)
            {
                Logs.Add(log);
            }
        }
        public void AddLog(LogItem item) => AddLog(item.ToString());
        public void AddLog(LogKey key, string name, string value) => AddLog(new LogItem(key, name, value));

        public static void CreateLogger(string filename)
        {
            Instance = new Logger(filename);
        }
    }
    public enum LogKey
    {
        Default = 0,
        DebugLog,
        StatisticsLog,
        Error,
    }
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

        public DateTime ReturnDateTimeFromTimeStamp => new DateTime(TimeStamp);

        public override string ToString()
        {
            return TimeStamp.ToString() + SeparatorString + Name + SeparatorString + Value;
        }

        public string ToHumanReadable()
        {
            return ReturnDateTimeFromTimeStamp.ToString() + SeparatorString + Name + SeparatorString + Value;
        }

        public static LogItem FromString(string loggedItem)
        {
            string[] split = loggedItem.Split(SeparatorArray, StringSplitOptions.None);
            var item = new LogItem
            {
                Name = split[1],
                Value = split[2],
                TimeStamp = Convert.ToInt64(split[0])
            };
            return item;
        }
    }

}