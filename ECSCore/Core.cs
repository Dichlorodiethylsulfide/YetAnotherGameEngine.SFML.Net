#define DEBUG_TIMINGS
//#undef DEBUG_TIMINGS

using System;
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
using ECS.Collision;
using ECS.Graphics;
using ECS.Physics;
using ECS.Strings;
using ECS.UI;
using ECS.Library;
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
        private struct FrameTimeDelay
        {
            public float DeltaTime0;
            public float DeltaTime1;
        }
        private static FrameTimeDelay FrameTime = new FrameTimeDelay();
        public static float DeltaTime
        {
            get
            {
                return FrameTime.DeltaTime1;
            }
            set
            {
                FrameTime.DeltaTime1 = FrameTime.DeltaTime0;
                FrameTime.DeltaTime0 = value;
            }
        }
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
                throw new Exception("Incompatible type.");
            ptr = cObject->GetDataPointer<T>();
        }
        /*
        private bool CheckValidity() => objectPtr != null && !objectPtr->IsNull() && ptr != null;
        public T VolatileRead()
        {
            if(CheckValidity())
                return *ptr;
            return default;
        }
        public void VolatileWrite(Ref<T> action)
        {
            if(CheckValidity())
                action(ref *ptr);
        }
        */
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
    /*
    internal struct SafePtr
    {
        public int hashCode;
        private IntPtr internalPtr;

        public unsafe static explicit operator CObject*(SafePtr ptr)
        {
            if(((CObject*)ptr.internalPtr)->GetHashCode() == ptr.hashCode)
                return (CObject*)ptr.internalPtr;
            return null;
        }
        public unsafe SafePtr(CObject* ptr)
        {
            internalPtr = (IntPtr)ptr;
            hashCode = ptr->GetHashCode();
        }
    }
    public unsafe struct ObjectIterator<T> where T : unmanaged
    {
        private int currentIndex;
        private int size;
        private IntPtr Items;

        internal ObjectIterator(int maxSize)
        {
            size = maxSize;
            currentIndex = 0;
            Items = Malloc(sizeof(SafePtr) * maxSize);
            MemSet(Items, 0, sizeof(SafePtr) * maxSize);
        }
        internal void Add(CObject* ptr)
        {
            fixed(IntPtr* p = &Items)
            {
                *(((SafePtr*)*p) + currentIndex++) = new SafePtr(ptr);
            }
        }
        public void Reset()
        {
            currentIndex = -1;
        }
        public void Do(Ref<T> action)
        {
            fixed(IntPtr* p = &Items)
            {
                while (++currentIndex < size)
                {
                    ( (CObject*)*( ( (SafePtr*)*p ) + currentIndex) )->WriteData(action);
                }
            }
            Reset();
        }
    }
    public ObjectIterator<T> GetAll<T>() where T : unmanaged
    {
        var index = GetDataTableIndexFromLookup<T>();
        var table = GetDataTable(index);
        var counter = 0;
        var iterator = new ObjectIterator<T>(table->Entries);
        fixed(CObject * object_ptr = &this.Value0)
        {
            for (int i = 0; i < this.MaxSize; i++)
            {
                var cObject = object_ptr + i;
                if (cObject->HasDataOf(index))
                {
                    iterator.Add(cObject);
                    counter++;
                }
                else if (counter >= table->Entries)//(i > this.Entries)
                {
                    // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                    break;
                }
            }
        }
        //iterator.Resize();
        iterator.Reset();
        return iterator;
    }
    */
    public struct CObject : IEquatable<CObject>, IDisposable
    {
        private static long incrementer = 1;
        public static readonly CObject Null = new CObject();

        internal long Index;
        internal bool NotDead; // Inverted so a null object will report as "Not NotDead" and so IsDefaultNull will report true

        internal IntPtr MetaPtr;
        //internal unsafe MetaSlot* Slot => (MetaSlot*)MetaPtr;
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
            throw new NullReferenceException();
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
            throw new NullReferenceException();
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
            throw new NullReferenceException();
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
            throw new NullReferenceException();
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
            throw new NullReferenceException();
        }
        /// <summary>
        /// Used to avoid AccessViolation and NullReference Exceptions
        /// </summary>
        /// <returns>False if object can be used, so !IsActiveNull() is the correct existence check, otherwise True</returns>
        public unsafe bool IsActiveNull() => !NotDead || this.ID <= 0 || MetaPtr == IntPtr.Zero || Meta->MaxSize == 0;
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
            NotDead = false;
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
                NotDead = true
                //MetaPtr = MetaCObject.New(),
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
            LookupTable.AddNewDataType<UI.Text>();
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
                    throw new Exception("Not enough space in table");
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
                    throw new ArgumentException("Invalid type.");
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
                        throw new ArgumentOutOfRangeException("index");
                    return (Hook*)*( ptrPtr + index );
                }
            }

            public static void AddNewDataType<T>() where T : unmanaged
            {
                if(!typeof(IComponentData).IsAssignableFrom(typeof(T)))
                {
                    throw new ArgumentException("Invalid Generic.");
                }
                TablePtr->addNewType(new T(), true, DefaultDataTableSize);
            }

            public static void AddNewType<T>(T empty, int size = 1024) where T : unmanaged => TablePtr->addNewType(empty, false, size);

            public static int GetDataTableIndex<T>()
            {
                if (DataTableLookup.TryGetValue(typeof(T), out var value))
                    return value;
                throw new ArgumentException("Invalid Generic.");
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

        /*
        internal struct Hook
        {
            public int DataType;
            public IntPtr GenericTypeHook;
        }
        internal struct HookIterator<T> where T : unmanaged
        {
            private Hook<T>* internalHook;
            private int currentIndex;
            private int totalReturned;
            private int expectedLimit;
            private bool limitReached;
            private Predicate<T> predicateCheck;
            private LinkableTable<T>* currentTable;

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
                                break;
                            }
                            currentIndex = 0;
                            currentTable = currentTable->Next;
                        }
                        if (currentTable->TryGetRef(internalHook->Empty, currentIndex++, out next) && predicateCheck(*next))
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
        internal struct Hook<T> where T : unmanaged
        {
            public int DataType;
            public int Count;
            public int TableSize;
            public T Empty;
            public LinkableTable<T>* Start;
            //Next available table
            //Last table

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
                for(int i = 0; i < Count; i++)
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
            private LinkableTable<T>* GetTable(int index)
            {
                if (index < 0 || index >= Count)
                    throw new Exception();
                var table = Start;
                for (int i = 0; i < index; i++)
                    table = table->Next;
                if (table == null)
                    throw new Exception();
                return table;
            }
            public T* FindRef(PredicateIn<T> predicate)
            {
                var table = Start;
                for (int i = 0; i < Count; i++)
                {
                    for (int j = 0; j < TableSize; j++)
                    {
                        if (predicate(in *table->GetRef(j)))
                        {
                            return table->GetRef(j);
                        }
                    }
                    table = table->Next;
                }
                return null;
            }
            public T Find(PredicateIn<T> predicate)
            {
                var item = FindRef(predicate);
                if (item == null)
                    throw new NullReferenceException();
                return *item;
            }

            public T* GetRef(int index)
            {
                var tuple = GetTableAndEntryIndexes(index);
                return GetTable(tuple.Item1)->GetRef(tuple.Item2);
            }

            public T Get(int index) => *GetRef(index);
            public int AddNew(T item)
            {
                fixed(LinkableTable<T>** start = &Start)
                {
                    var index = 0;
                    var table = *start;
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
                        throw new Exception();
                    if (table->Next != null)
                        throw new Exception();

                    NewTable(table);
                    index += table->Next->AddNew(item);
                    return index;
                }
            }
            public void RemoveAt(int index)
            {
                if (index < 0 || index >= MaxCapacity)
                    throw new Exception();
                var tuple = GetTableAndEntryIndexes(index);
                var table = Start;
                for (int i = 0; i < tuple.Item1; i++)
                    table = table->Next;
                if (table == null)
                    throw new Exception();
                table->RemoveAt(tuple.Item2, Empty);
                if (table->Count == 0 && table->WriteIndex == table->MaxSize)
                {
                    table->WriteIndex = 0;
                }
            }
            private void NewTable(LinkableTable<T>* table)
            {
                table->Next = LinkableTable<T>.NewTable(table, TableSize, Empty);
                Count++;
            }
            public void AppendNewTable()
            {
                var table = Start;
                while (table->Next != null)
                    table = table->Next;
                NewTable(table);
            }

            public void DebugHook()
            {
                var total = 0;
                var table = Start;
                for (int i = 0; i < Count; i++)
                {
                    total += table->DebugTable(Empty);
                    table = table->Next;
                }
                Console.WriteLine("Total Tables: " + Count);
                Console.WriteLine("Total Objects: " + total);
            }

            public static void NewTableChain(T empty, int size = 1024)
            {
                var outer = Malloc(sizeof(Hook));
                MemSet(outer, 0, sizeof(Hook));
                var outerHook = (Hook*)outer;
                outerHook->DataType = typeof(T).GetHashCode();
                var bytes = sizeof(Hook<T>);
                outerHook->GenericTypeHook = Malloc(bytes);
                MemSet(outerHook->GenericTypeHook, 0, bytes);
                var hook = (Hook<T>*)outerHook->GenericTypeHook;
                hook->DataType = outerHook->DataType;
                hook->Count = 1;
                hook->TableSize = size;
                hook->Empty = empty;
                hook->Start = LinkableTable<T>.NewTable(null, size, hook->Empty);
                TablePtr->Add(outer);
            }
        }
        internal struct LinkableTable<T> where T : unmanaged
        {
            public int ReadIndex;
            public int WriteIndex;
            public int Count;
            public int MaxSize;
            public int AllocatedBytes;
            public LinkableTable<T>* Previous;
            public LinkableTable<T>* Next;
            public T Value0;

            public int DebugTable(T empty)
            {
                fixed (T* ptr = &Value0)
                {
                    var counter = 0;
                    for (int i = 0; i < MaxSize; i++)
                    {
                        var Object = ptr + i;
                        if (!Object->Equals(empty))
                            counter++;
                    }
                    Console.WriteLine(counter + " / " + Count);
                    return counter;
                }
            }

            public void ClearTable(T empty)
            {
                fixed (T* ptr = &Value0)
                {
                    for (int i = 0; i < MaxSize; i++)
                    {
                        *( ptr + i ) = empty;
                    }
                }
            }

            public int AddNew(T item)
            {
                if (WriteIndex >= MaxSize || Count >= MaxSize)
                    throw new Exception();
                fixed (T* ptr = &Value0)
                {
                    *( ptr + WriteIndex++ ) = item;
                    Count++;
                }
                return WriteIndex - 1;
            }

            public void RemoveAt(int index, T empty)
            {
                if (index < 0 || index >= MaxSize)
                    throw new Exception();
                fixed (T* ptr = &Value0)
                {
                    if (( ptr + index )->Equals(empty))
                        return;
                    *( ptr + index ) = empty;
                    Count--;
                }
            }

            public bool TryGetRef(T empty, int index, out T* ptr)
            {
                ptr = GetRef(index);
                return !ptr->Equals(empty);
            }

            public T* GetRef(int index)
            {
                if (index < 0 || index >= MaxSize)
                    throw new Exception();
                fixed (T* ptr = &Value0)
                {
                    return ptr + index;
                }
            }
            public T Get(int index)
            {
                return *GetRef(index);
            }

            public static LinkableTable<T>* NewTable(LinkableTable<T>* previous, int size, T empty)
            {
                var bytes = ( sizeof(int) * 5 ) + ( sizeof(LinkableTable<T>*) * 2 ) + ( sizeof(T) * size );
                var entry = Malloc(bytes);
                MemSet(entry, 0, bytes);
                var table = (LinkableTable<T>*)entry;
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
        */
        internal struct HookIterator<T> where T : unmanaged
        {
            private Hook* internalHook;
            private int currentIndex;
            private int totalReturned;
            private int expectedLimit;
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
                                break;
                            }
                            currentIndex = 0;
                            currentTable = currentTable->Next;
                        }
                        if (currentTable->TryGetRef<T>(new T(), currentIndex++, out next) && predicateCheck(*next))
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
                    throw new Exception();
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
                    throw new NullReferenceException();
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
                    throw new Exception();
                if (table->Next != null)
                    throw new Exception();

                NewTable<T>(table);
                index += table->Next->AddNew(item);
                return index;
            }
            public void RemoveAt<T>(int index) where T : unmanaged
            {
                if (index < 0 || index >= MaxCapacity)
                    throw new Exception();
                var tuple = GetTableAndEntryIndexes(index);
                var table = Start;
                for (int i = 0; i < tuple.Item1; i++)
                    table = table->Next;
                if (table == null)
                    throw new Exception();
                table->RemoveAt(tuple.Item2, new T());
                if (table->Count == 0 && table->WriteIndex == table->MaxSize)
                {
                    table->WriteIndex = 0;
                }
            }
            public void ByteRemovalAt(int index)
            {
                if (index < 0 || index >= MaxCapacity)
                    throw new Exception();
                var tuple = GetTableAndEntryIndexes(index);
                var table = Start;
                for (int i = 0; i < tuple.Item1; i++)
                    table = table->Next;
                if (table == null)
                    throw new Exception();
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
                    throw new Exception();
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
                    throw new Exception();
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
                    throw new Exception();
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
                    throw new Exception();
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
        internal struct TextureEntry : IDisposable
        {
            public static readonly TextureEntry Null = new TextureEntry();
            public int HashCode;
            public IntRect TextureRect;
            public IntPtr TexturePtr;
            public Vector2u GetSize() => SFMLTexture.sfTexture_getSize(TexturePtr);
            public void Dispose()
            {
                Free(TexturePtr);
            }
            public static TextureEntry New(string filename)
            {
                var rect = new IntRect();
                var entry = new TextureEntry
                {
                    TexturePtr = SFMLTexture.sfTexture_createFromFile(filename, ref rect),
                    TextureRect = rect,
                    HashCode = filename.GetHashCode()
                };
                return entry;
            }
        }
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
                var iterator = HookIterator<CObject>.GetIterator(tableCount, x => x.HasDataOf(index));
                var next = iterator.Next;
                while(next != null)
                {
                    action(ref *next->GetDataPointer<T>());
                    next = iterator.Next;
                }
            }
            public static void Iterate<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
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
                    action(ref *next->GetDataPointer<T>(),
                           ref *next->GetDataPointer<U>());
                    next = iterator.Next;
                }
            }

            public static void IterateWithObject<T, U>(RefWithObject<T, U, CObject> action, bool mutli_threaded = false) where T : unmanaged where U : unmanaged
            {
                var index = LookupTable.GetDataTableIndex<T>();
                var index2 = LookupTable.GetDataTableIndex<U>();

                var tableCount = LookupTable.GetDataTable<T>()->TotalHookCount();
                var tableCount2 = LookupTable.GetDataTable<U>()->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

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
            public static void PossibleCollisionQuery()
            {
                PossibleCollisionsThisFrame.Clear();
                var index = LookupTable.GetDataTableIndex<Transform>();
                var index2 = LookupTable.GetDataTableIndex<Collider>();

                var tableCount = LookupTable.GetDataTableFromIndex(index)->TotalHookCount();
                var tableCount2 = LookupTable.GetDataTableFromIndex(index2)->TotalHookCount();

                var size = Math.Min(tableCount, tableCount2);

                var iterator = HookIterator<CObject>.GetIterator(size, x => x.HasDataOf(index) && x.HasDataOf(index2));
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
                iterator.Reset();
                next = iterator.Next;
                while(next != null)
                {
                    if (Tree->QueryOverlaps(next) is List<CObject> list && list.Count > 0)
                        PossibleCollisionsThisFrame.Add(*next, list);
                    next = iterator.Next;
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
                        }

                        if (physics_enabled)
                        {
                            // Get physics body and velocity

                            var this_pos = transform->Position;
                            var other_pos = other->Position;

                            transform->Position -= this_pos.Direction(other_pos) * delta;
                            other->Position -= other_pos.Direction(this_pos) * delta;
                        }
                    }
                }
            }
        }
        public struct Textures
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
                var entry = TextureTablePtr->FindRef((in TextureEntry x) => x.HashCode == filename.GetHashCode());
                if (entry == null)
                    return IntPtr.Zero;
                return (IntPtr)entry;
            }
            public static IntPtr NewTexture(string filename)
            {
                var index = TextureTablePtr->AddNew(TextureEntry.New(filename));
                return (IntPtr)TextureTablePtr->GetRef<TextureEntry>(index);
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
        /*
        private const int DataTableStartIndex = 3; // Object Table at 0, Texture table will be 1, collisions is 2, animations will be 3, so should be 4 soon
        private static int DefaultDataTableSize = 0;
        private static IntPtr Table = IntPtr.Zero;
        internal static LookupTable* TablePtr => (LookupTable*)Table;
        internal static int DataTableEntries => TablePtr->Entries - DataTableStartIndex;
        internal static ObjectTable* ObjectTablePtr => (ObjectTable*)*&TablePtr->Value0;
        internal static TextureTable* TextureTablePtr => (TextureTable*)*((&TablePtr->Value0) + 1);
        internal static AABBTree* Tree => (AABBTree*)*((&TablePtr->Value0) + 2);

        //internal static Thread ProgenitorThread = null;

        private static RefObjectTable refObjects = default;
        public static RefObjectTable Entities => refObjects;
        public static Dictionary<CObject, List<CObject>> PossibleCollisionsThisFrame => ObjectTable.PossibleCollisionsThisFrame;
        public static Dictionary<CObject, List<CObject>> VerifiedCollisionsThisFrame => ObjectTable.VerifiedCollisionsThisFrame;

        internal static DataTable* GetDataTable(int index)
        {
            index += DataTableStartIndex;
            if (index < DataTableStartIndex || index >= TablePtr->Entries) // check entries against datatablestartindex just in case
                throw new NullReferenceException(nameof(index));
            return (DataTable*)( *( &TablePtr->Value0 + index) );
        }
        
        internal static DataTable* GetDataTableOf(int type)
        {
            for(int i = 0; i < DataTableEntries; i++)
            {
                var table = GetDataTable(i);
                if(table->DataType == type)
                    return table;
            }
            throw new ArgumentException(nameof(type));
        }
        internal static int GetDataTableIndexOf(int type)
        {
            for (int i = 0; i < DataTableEntries; i++)
            {
                var table = GetDataTable(i);
                if (table->DataType == type)
                    return i;
            }
            throw new ArgumentException(nameof(type));
        }
        
        internal static int GetDataTableIndexOfGeneric<T>() where T : unmanaged => GetDataTableIndexOf(typeof(T).GetHashCode());

        internal static int GetDataTableIndexFromLookup<T>() where T : unmanaged => DataTableLookup[typeof(T)];
        //internal static int GetDataTableIndexFromLookup<T>() where T : unmanaged => GetDataTableIndexOfGeneric<T>();


        private static readonly Dictionary<Type, int> DataTableLookup = new Dictionary<Type, int>();

        public static void Entry(int mainSize = 64, int objectSize = 1024, int defaultDataSize = 1024, int textureSize = 64)
        {
            //ProgenitorThread = Thread.CurrentThread;
            CreateMainTable(mainSize + DataTableStartIndex);
            CreateObjectTable(objectSize);
            CreateTextureTable(textureSize);
            CreateTree(objectSize);
            refObjects = RefObjectTable.New();
            DefaultDataTableSize = defaultDataSize;
            AddNewDataType<Texture>();
            AddNewDataType<Transform>();
            AddNewDataType<CString>();
            AddNewDataType<Button>();
            AddNewDataType<UI.Text>();
            Tree->transformDataTableIndex = GetDataTableIndexFromLookup<Transform>();
        }

        public static void AddNewDataType<T>() where T : unmanaged
        {
            if(TablePtr->Entries >= TablePtr->MaxSize)
            {
                throw new Exception("Not enough space in table");
            }
            var type = typeof(T);
            if(!typeof(IComponentData).IsAssignableFrom(type) || !type.IsValueType)
            {
                throw new ArgumentException("Invalid generic type.");
            }
            var hash = type.GetHashCode();
            for(int i = 0; i < DataTableEntries; i++)
            {
                if(GetDataTable(i)->DataType == hash)
                {
                    throw new ArgumentException("Type already exists.");
                }
            }
            //if (DataTableLookup.TryGetValue(type, out var data))
            //    throw new ArgumentException("Type already exists.");
            CreateDataTable<T>(DefaultDataTableSize);
            DataTableLookup.Add(typeof(T), GetDataTableIndexOfGeneric<T>());
        }
        private static void CreateTree(int size = 1024)
        {
            var bytes = sizeof(AABBTree);
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var tree = (AABBTree*)entry;
            *tree = new AABBTree(size);
            TablePtr->SetNext(entry);
        }

        private static void CreateMainTable(int size = 1024)
        {
            var bytes = (sizeof(int) * 3) + (size * sizeof(IntPtr));
            Table = Malloc(bytes);
            MemSet(Table, 0, bytes);
            *TablePtr = new LookupTable();
            TablePtr->Entries = 0;
            TablePtr->MaxSize = size;
            TablePtr->AllocatedBytes = bytes;
        }
        private static void CreateObjectTable(int size = 1024)
        {
            var bytes = ( sizeof(int) * 5 ) + ( size * sizeof(CObject) );
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var table = (ObjectTable*)entry;
            *table = new ObjectTable();
            table->Count = 0;
            table->ReadIndex = 0;
            table->WriteIndex = 0;
            table->MaxSize = size;
            table->AllocatedBytes = bytes;
            TablePtr->SetNext(entry);
        }

        private static void AddNewObject(CObject cObject)
        {
            if (ObjectTablePtr->WriteIndex >= ObjectTablePtr->MaxSize || ObjectTablePtr->Count >= ObjectTablePtr->MaxSize)
            {
                //RecreateObjectTable(ObjectTablePtr->MaxSize * 2);
                Debug.Breakpoint();
                //var ptr = &table->Value0;
                //CGC.ResizeEntireBlock((IntPtr)ptr, sizeof(CObject), table->MaxSize, table->MaxSize * 2);
                //RecreateTree(table->MaxSize * 2, 2, true);
                //throw new Exception("Not enough space in table"); // change, relocate writeindex if invalid, realloc to remove dead space --- do same for all tables
            }
            ObjectTablePtr->SetNext(cObject);
        }
        internal static CObject AddNewObject(long index)
        {
            var obj = CObject.New(index);
            AddNewObject(obj);
            return obj;
        }
        internal static void AddObjectData<T>(CObject cObject, T data) where T : unmanaged
        {
            AddNewData(GetDataTable(GetDataTableIndexFromLookup<T>()), cObject, data);
        }
        private static void AddNewData<T>(DataTable* table, CObject cObject, T data) where T : unmanaged
        {
            var type = typeof(T).GetHashCode();
            if (table->DataType != type)
            {
                throw new Exception("Data type mismatch.");
            }
            if (table->WriteIndex >= table->MaxSize || table->Count >= table->MaxSize)
            {
                //throw new Exception("Not enough space in table"); // change, relocate writeindex if invalid, realloc to remove dead space --- do same for all tables
                //RecreateDataTable<T>(table->MaxSize * 2);
                //table = GetDataTable(GetDataTableIndexOf(type));
                Debug.Breakpoint();
            }
            var entry = new Data
            {
                DataIndex = table->WriteIndex,
                DataType = type,
                IsInitialised = true,
                DataSize = sizeof(T),
                ObjectIndex = (int)cObject.Index // check this just in case, it appears to work so far
            };
            entry.DataPtr = Malloc(entry.DataSize);
            MemSet(entry.DataPtr, 0, entry.DataSize);
            *(T*)entry.DataPtr = data;
            table->SetNext(entry);
            cObject.Slot->Insert(GetDataTableIndexFromLookup<T>(), table->GetLast());
        }
        private static void CreateDataTable<T>(int size = 1024) where T : unmanaged
        {
            var bytes = ( sizeof(int) * 6 ) + ( size * sizeof(Data) );
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var table = (DataTable*)entry;
            *table = new DataTable();
            table->Count = 0;
            table->ReadIndex = 0;
            table->WriteIndex = 0;
            table->MaxSize = size;
            table->AllocatedBytes = bytes;
            table->DataType = typeof(T).GetHashCode();
            TablePtr->SetNext(entry);
        }
        private static void CreateTextureTable(int size = 1024)
        {
            var bytes = ( sizeof(int) * 5 ) + ( size * sizeof(TextureEntry) );
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var table = (TextureTable*)entry;
            *table = new TextureTable();
            table->Count = 0;
            table->ReadIndex = 0;
            table->WriteIndex = 0;
            table->MaxSize = size;
            table->AllocatedBytes = bytes;
            TablePtr->SetNext(entry);
        }
        internal struct LookupTable
        {
            public int Entries;
            public int MaxSize;
            public int AllocatedBytes;
            public IntPtr Value0;

            public IntPtr GetPointerAtIndex(int index)
            {
                fixed(IntPtr* ptr = &this.Value0)
                {
                    if(index < 0 || index >= this.MaxSize)
                        return IntPtr.Zero;
                    return ptr[index];
                }
            }
            public void SetPointerAtIndex(int index, IntPtr new_ptr)
            {
                fixed(IntPtr *ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new ArgumentException();
                    *(ptr + index) = new_ptr;
                }
            }
            public void SetNext(IntPtr new_ptr) => SetPointerAtIndex(Entries++, new_ptr);
        }
        internal struct ObjectTable
        {
            public int ReadIndex;
            public int WriteIndex;
            public int Count;
            public int MaxSize;
            public int AllocatedBytes;
            public CObject Value0;

            public CObject GetObjectAtIndex(int index)
            {
                fixed (CObject* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new NullReferenceException(nameof(index));
                    return ptr[index];
                }
            }
            public IntPtr GetObjectPointerAtIndex(int index)
            {
                fixed (CObject* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new NullReferenceException(nameof(index));
                    return (IntPtr)(ptr + index);
                }
            }
            public void SetObjectAtIndex(int index, CObject cObject)
            {
                fixed (CObject* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new ArgumentException();
                    *( ptr + index ) = cObject;
                }
            }
            public void SetNext(CObject cObject)
            {
                SetObjectAtIndex(WriteIndex++, cObject);
                Count++;
            }

            public void RemoveObjectAtIndex(int index)
            {
                fixed (CObject* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new ArgumentException();
                    var destroyed = ptr + index;
                    if (destroyed->MetaPtr == IntPtr.Zero)
                        return;
                    destroyed->InternalDispose();
                    MemSet((IntPtr)destroyed, 0, sizeof(CObject));
                    Count--;
                }
            }
            public CObject Get<T>(Compare<T> comparer) where T : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var table = GetDataTable(index);
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index) && comparer(cObject->GetData<T>()))
                        {
                            return *cObject;
                        }
                    }
                }
                return CObject.Null;
            }
            public ByRefObject GetRef<T>(Compare<T> comparer) where T : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var table = GetDataTable(index);
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index) && comparer(cObject->GetData<T>()))
                        {
                            return new ByRefObject(cObject);
                        }
                    }
                }
                return ByRefObject.Null;
            }
            public ByRefData<T> GetRef<T, U>(Compare<U> comparer) where T : unmanaged where U : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var table = GetDataTable(index);
                var index2 = GetDataTableIndexFromLookup<U>();
                var table2 = GetDataTable(index2);

                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index) && cObject->HasDataOf(index2) && comparer(cObject->GetData<U>()))
                        {
                            return new ByRefData<T>(cObject);
                        }
                    }
                }
                return ByRefData<T>.Null;
            }
            public T Get<T, U>(Compare<U> comparer) where T : unmanaged where U : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var table = GetDataTable(index);
                var index2 = GetDataTableIndexFromLookup<U>();
                var table2 = GetDataTable(index2);

                fixed(CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if(cObject->HasDataOf(index) && cObject->HasDataOf(index2) && comparer(cObject->GetData<U>()))
                        {
                            return cObject->GetData<T>();
                        }
                    }
                }
                return default;
                //throw new NullReferenceException();
            }

            public void IterateTable<T>(RefWithObject<T, CObject> action, bool multi_threaded = false) where T : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var table = GetDataTable(index);
                var counter = table->ReadIndex;
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index))
                        {
                            cObject->Slot->Get(index)->UseInternalData(action);
                            counter++;
                        }
                        else if (counter >= table->Count)//(i > this.Entries)
                        {
                            // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                            // Check for consecutive dead space to realloc away during GC call
                            return;
                        }
                    }
                }

            }
            public void IterateTable<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var table = GetDataTable(index);
                var counter = table->ReadIndex;
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index))
                        {
                            cObject->Slot->Get(index)->UseInternalData(action);
                            counter++;
                        }
                        else if (counter >= table->Count)//(i > this.Entries)
                        {
                            // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                            return;
                        }
                    }
                }
            }

            public void IterateTable<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var index2 = GetDataTableIndexFromLookup<U>();

                var table = GetDataTable(index);
                var table2 = GetDataTable(index2);

                var size = Math.Min(table->Count, table2->Count);
                var maxSize = this.MaxSize;

                var counter = Math.Min(table->ReadIndex, table2->ReadIndex);

                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < maxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index) && cObject->HasDataOf(index2))
                        {
                            action(ref *cObject->Slot->Get(index)->ExposeData<T>(),
                                   ref *cObject->Slot->Get(index2)->ExposeData<U>());
                            counter++;
                        }
                        else if (counter >= size)
                        {
                            // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                            return;
                        }
                    }
                }
            }

            public void IterateTable<T, U>(RefWithObject<T, U, CObject> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                var index = GetDataTableIndexFromLookup<T>();
                var index2 = GetDataTableIndexFromLookup<U>();

                var table = GetDataTable(index);
                var table2 = GetDataTable(index2);

                var size = Maths.Maths.Min(table->Count, table2->Count);

                var counter = Math.Min(table->ReadIndex, table2->ReadIndex);

                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index) && cObject->HasDataOf(index2))
                        {

                            action(ref *cObject->Slot->Get(index)->ExposeData<T>(),
                                   ref *cObject->Slot->Get(index2)->ExposeData<U>(),
                                   ref *cObject);
                            counter++;
                        }
                        else if (counter >= size)
                        {
                            return;
                        }
                    }
                }
            }

            public void IterateTable<T, U, V>(RefRefRef<T, U, V> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged where V : unmanaged
            {
                var indexes = new int[3] { GetDataTableIndexFromLookup<T>(), GetDataTableIndexFromLookup<U>(), GetDataTableIndexFromLookup<V>() };

                var table = GetDataTable(indexes[0]);
                var table2 = GetDataTable(indexes[1]);
                var table3 = GetDataTable(indexes[2]);

                var size = Math.Min(table->Count, table2->Count);
                size = Math.Min(size, table3->Count);

                var counter = Math.Min(table->ReadIndex, table2->ReadIndex);
                counter = Math.Min(size, table3->ReadIndex);

                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOfAll(indexes))
                        {
                            action(ref *cObject->Slot->Get(indexes[0])->ExposeData<T>(),
                                   ref *cObject->Slot->Get(indexes[1])->ExposeData<U>(),
                                   ref *cObject->Slot->Get(indexes[2])->ExposeData<V>());
                            counter++;
                        }
                        else if (counter >= size)//(i > this.Entries)
                        {
                            // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                            return;
                        }
                    }
                    
                }
            }
            public static Dictionary<CObject, List<CObject>> VerifiedCollisionsThisFrame { get; private set; } = new Dictionary<CObject, List<CObject>>();
            public static Dictionary<CObject, List<CObject>> PossibleCollisionsThisFrame { get; private set; } = new Dictionary<CObject, List<CObject>>();
            public void PossibleCollisionQuery(bool multi_threaded = false)
            {
                PossibleCollisionsThisFrame = new Dictionary<CObject, List<CObject>>();

                var index = GetDataTableIndexFromLookup<Transform>();
                var index2 = GetDataTableIndexFromLookup<Collider>();

                var table = GetDataTable(index);
                var table2 = GetDataTable(index2);

                var size = Math.Min(table->Count, table2->Count);

                var counter = Math.Min(table->ReadIndex, table2->ReadIndex);

                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index) && cObject->HasDataOf(index2))
                        {
                            Collider* data = cObject->Slot->Get(index2)->ExposeData<Collider>();
                            if (!data->IsInTree)
                            {
                                Tree->InsertObject(cObject);
                                data->IsInTree = true;
                            }
                            else
                            {
                                Tree->UpdateObject(cObject);
                            }
                            counter++;
                        }
                        else if (counter >= size)
                        {
                            break;
                        }
                    }
                    // Return collisions -- can perform this operation on multiple threads, not really any faster though
                    counter = Math.Min(table->ReadIndex, table2->ReadIndex);

                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(index) && cObject->HasDataOf(index2))
                        {
                            if (Tree->QueryOverlaps(cObject) is List<CObject> list && list.Count > 0)
                                PossibleCollisionsThisFrame.Add(*cObject, list);
                            counter++;
                        }
                        else if (counter >= size)
                        {
                            break;
                        }
                    }
                    
                }
            }

            public void VerifyCollisions(bool physics_enabled, bool multi_threaded = false)
            {
                if (PossibleCollisionsThisFrame.Count == 0)
                    return;
                VerifiedCollisionsThisFrame.Clear();
                var delta = CTime.DeltaTime;
                foreach (var cObject in PossibleCollisionsThisFrame.Keys)
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
                            if(VerifiedCollisionsThisFrame.ContainsKey(cObject))
                            {
                                VerifiedCollisionsThisFrame[cObject].Add(list[i]);
                            }
                            else
                            {
                                VerifiedCollisionsThisFrame.Add(cObject, new List<CObject> { list[i] });
                            }
                        }

                        if(physics_enabled)
                        {
                            // Get physics body and velocity

                            var this_pos = transform->Position;
                            var other_pos = other->Position;

                            transform->Position -= this_pos.Direction(other_pos) * delta;
                            other->Position -= other_pos.Direction(this_pos) * delta;
                        }
                    }
                }
            }
        }
        public struct RefObjectTable
        {
            private IntPtr table;

            //public ObjectIterator<T> GetAll<T>() where T : unmanaged => ( (ObjectTable*)table )->GetAll<T>();
            public CObject Get(string name) => Get((in CString _string) => _string.Equals(name));
            public ByRefObject GetRef(string name) => GetRef((in CString _string) => _string.Equals(name));
            public T Get<T>(string name) where T : unmanaged => Get<T, CString>((in CString _string) => _string.Equals(name));
            public ByRefData<T> GetRef<T>(string name) where T : unmanaged => GetRef<T, CString>((in CString _string) => _string.Equals(name));
            public CObject Get<T>(Compare<T> comparer) where T : unmanaged
            {
                return ( (ObjectTable*)table )->Get(comparer);
            }
            public ByRefObject GetRef<T>(Compare<T> comparer) where T : unmanaged
            {
                return ((ObjectTable*)table)->GetRef(comparer);
            }
            public T Get<T, U>(Compare<U> comparer) where T : unmanaged where U : unmanaged
            {
                return ((ObjectTable*)table)->Get<T, U>(comparer);
            }
            public ByRefData<T> GetRef<T, U>(Compare<U> comparer) where T : unmanaged where U : unmanaged
            {
                return ( (ObjectTable*)table )->GetRef<T, U>(comparer);
            }
            public void With<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                RefDataTable.With(action, multi_threaded);
            }
            public void Iterate<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            public void IterateWithObject<T>(RefWithObject<T, CObject> action, bool multi_threaded = false) where T : unmanaged
            {
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            public void IterateWithObject<T, U>(RefWithObject<T, U, CObject> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            public void Iterate<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            public void Iterate<T, U, V>(RefRefRef<T, U, V> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged where V : unmanaged
            {
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            
            public void PossibleCollisionQuery() => ( (ObjectTable*)table )->PossibleCollisionQuery();
            public void VerifyCollisions(bool physics_enabled) => ( (ObjectTable*)table )->VerifyCollisions(physics_enabled);
            public static RefObjectTable New()
            {
                var refTable = new RefObjectTable()
                {
                    table = (IntPtr)ObjectTablePtr,
                };
                return refTable;
            }
        }
        public delegate void Ref<T0>(ref T0 data0);
        public delegate void RefWithObject<T0, O0>(ref T0 data0, ref O0 object0);
        //public delegate void RefWithIteration<T0>(ref T0 data0, int current_iteration, int max_iteration);
        public delegate void RefRef<T0, T1>(ref T0 data0, ref T1 data1);
        public delegate void RefWithObject<T0, T1, O0>(ref T0 data0, ref T1 data1, ref O0 object0);
        public delegate void RefRefRef<T0, T1, T2>(ref T0 data0, ref T1 data1, ref T2 data2);
        public delegate void In<T0>(in T0 data0);
        public delegate bool Compare<T0>(in T0 data);

        internal struct RefDataTable
        {
            public static void With<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                GetDataTable(GetDataTableIndexFromLookup<T>())->IterateTable(action, multi_threaded);
            }
        }
        internal struct DataTable
        {
            public int DataType;
            public int ReadIndex;
            public int WriteIndex;
            public int Count;
            public int MaxSize;
            public int AllocatedBytes;
            public Data Value0;

            public Data GetDataAtIndex(int index)
            {
                fixed (Data* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new NullReferenceException(nameof(index));
                    return ptr[index];
                }
            }

            public IntPtr GetDataPointerAtIndex(int index)
            {
                fixed (Data* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new NullReferenceException(nameof(index));
                    return (IntPtr)(ptr + index);
                }
            }

            public void SetDataAtIndex(int index, Data data)
            {
                fixed (Data* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new ArgumentException();
                    *( ptr + index ) = data;
                }
            }

            public void RemoveDataAtIndex(int index)
            {
                fixed (Data* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new ArgumentException();
                    ( ptr + index )->Dispose();
                    MemSet((IntPtr)( ptr + index ), 0, sizeof(Data)); // just reset it to 0 for now
                    Count--;
                }
            }

            public void SetNext(Data data)
            {
                //SetDataAtIndex(Entries++, data);
                SetDataAtIndex(WriteIndex++, data);
                Count++;
            }

            public IntPtr GetLast() => GetDataPointerAtIndex(WriteIndex - 1);

            public void IterateTable<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                fixed (Data* data_ptr = &this.Value0)
                {
                    var counter = ReadIndex;
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var data = data_ptr + i;
                        if (data->IsValid)
                        {
                            data->UseInternalData(action);
                            counter++;
                        }
                        if (counter >= this.Count)
                            return;
                    }
                }
            }
        }
        
        public static IntPtr TryAddTexture(string filename)
        {
            try
            {
                return TextureTablePtr->Find(filename);
            }
            catch (Exception)
            {
                return TextureTablePtr->NewTexture(filename);
            }
        }
        internal struct TextureTable
        {
            public int ReadIndex;
            public int WriteIndex;
            public int Count;
            public int MaxSize;
            public int AllocatedBytes;
            public TextureEntry Value0;

            public TextureEntry GetEntryAtIndex(int index)
            {
                fixed (TextureEntry* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new ArgumentException();
                    return ptr[index];
                }
            }

            public IntPtr GetEntryPointerAtIndex(int index)
            {
                fixed (TextureEntry* ptr = &this.Value0)
                {
                    if (index < 0 || index >= this.MaxSize)
                        throw new ArgumentException();
                    return (IntPtr)( ptr + index );
                }
            }

            public void SetEntryAtIndex(int index, TextureEntry new_ptr)
            {
                fixed (TextureEntry* ptr = &this.Value0)
                {
                    if (index < 0 || index > this.MaxSize)
                        throw new ArgumentException();
                    *( ptr + index ) = new_ptr;
                }
            }

            public void SetNext(TextureEntry entry)
            {
                //SetEntryAtIndex(Entries++, entry);
                SetEntryAtIndex(WriteIndex++, entry);
                Count++;
            }
            public IntPtr GetLast() => GetEntryPointerAtIndex(WriteIndex - 1);
            public IntPtr Find(string filename)
            {
                var hash = filename.GetHashCode();
                fixed(TextureEntry* ptr = &this.Value0)
                {
                    for(int i = ReadIndex; i < Count; i++)
                    {
                        if((ptr + i)->HashCode == hash)
                        {
                            return (IntPtr)ptr;
                        }
                    }
                }
                throw new Exception("File not found.");
            }

            public IntPtr NewTexture(string filename)
            {
                if (Count >= MaxSize)
                {
                    throw new Exception("Not enough space in table");
                }
                var rect = new IntRect();
                var entry = new TextureEntry();
                entry.TexturePtr = SFMLTexture.sfTexture_createFromFile(filename, ref rect);
                entry.TextureRect = rect;
                entry.HashCode = filename.GetHashCode();
                TextureTablePtr->SetNext(entry);
                return TextureTablePtr->GetLast();
            }
        }
        public struct TextureEntry : IDisposable
        {
            public int HashCode;
            public IntRect TextureRect;
            public IntPtr TexturePtr;

            public Vector2u GetSize() => SFMLTexture.sfTexture_getSize(TexturePtr);

            public void Dispose()
            {
                Free(TexturePtr);
            }
        }
        internal struct Data : IDisposable
        {
            public static readonly Data Null = new Data();

            public bool IsInitialised;
            public int DataType;
            public int DataSize;
            public int DataIndex;
            public IntPtr DataPtr;
            public int ObjectIndex;
            public bool IsValid => IsInitialised && DataPtr != IntPtr.Zero && ObjectIndex != -1;
            public T GetInternalData<T>() where T : unmanaged
            {
                if (IsValid)
                    return *(T*)DataPtr;
                throw new NullReferenceException();
            }
            public T* ExposeData<T>() where T : unmanaged => (T*)this.DataPtr;
            public void UseInternalData<T>(Ref<T> action) where T : unmanaged => action(ref *(T*)this.DataPtr);
            //public void UseInternalData<T>(RefWithIteration<T> action, int index, int max) where T : unmanaged => action(ref *(T*)this.DataPtr, index, max);
            public void UseInternalData<T>(In<T> action) where T : unmanaged => action(in *(T*)this.DataPtr);
            public void UseInternalData<T>(RefWithObject<T, CObject> action) where T : unmanaged => action(ref *(T*)this.DataPtr, ref *(CObject*)ObjectTablePtr->GetObjectPointerAtIndex(ObjectIndex));

            public void Dispose()
            {
                if (DataType == typeof(Collider).GetHashCode())
                    Tree->RemoveObject((CObject*)ObjectTablePtr->GetObjectPointerAtIndex(ObjectIndex));
                // Kill link to object --- clear object's meta data cache
                ObjectIndex = -1;
                // Kill link to data --- clear data's data
                Free(DataPtr);
                DataPtr = IntPtr.Zero;
            }

            public static void Destroy(Data data)
            {
                GetDataTableOf(data.DataType)->RemoveDataAtIndex(data.DataIndex);
            }
        }
        
        internal struct MetaSlot : IDisposable
        {
            public int MaxSize;
            public IntPtr DataPtr;
            public Data* Get(int index)
            {
                if (index < 0 || index >= MaxSize)
                    return null;
                fixed (IntPtr* ptrPtr = &DataPtr)
                {
                    return (Data*)*( ptrPtr + index );
                }
            }
            public void Insert(int index, IntPtr ptr)
            {
                if (index < 0 || index >= MaxSize)
                    return;
                fixed(IntPtr* ptrPtr = &DataPtr)
                {
                    *( ptrPtr + index ) = ptr;
                }
            }
            public void Remove(int index)
            {
                if (index < 0 || index >= MaxSize)
                    return;
                fixed (IntPtr* ptrPtr = &DataPtr)
                {
                    *( ptrPtr + index ) = IntPtr.Zero;
                }
            }
            public bool HasDataOf(int index)
            {
                if (index < 0 || index >= MaxSize)
                    return false;
                fixed (IntPtr* ptrPtr = &DataPtr)
                {
                    return *( ptrPtr + index ) != IntPtr.Zero;
                }
            }
            public bool HasDataOfAll(int[] indexes)
            {
                fixed (IntPtr* ptrPtr = &DataPtr)
                {
                    for(int i = 0; i < indexes.Length; i++)
                    {
                        var index = indexes[i];
                        if (index < 0 || index >= MaxSize)
                            return false;
                        if (*( ptrPtr + index ) == IntPtr.Zero)
                            return false;
                    }
                    return true;
                }
            }
            public static IntPtr New()
            {
                var size = TablePtr->Entries - DataTableStartIndex + 1;
                var bytes = sizeof(int) + (sizeof(IntPtr) * size );
                var ptr = Malloc(bytes);
                MemSet(ptr, 0, bytes);
                var metaSlot = new MetaSlot()
                {
                    MaxSize = size
                };
                *(MetaSlot*)ptr = metaSlot;
                return ptr;
            }

            public void Dispose()
            {
                fixed (IntPtr* ptr = &DataPtr)
                {
                    for (int i = 0; i < this.MaxSize - 1; i++)
                    {
                        var int_ptr = *( ptr + i );
                        if (int_ptr != IntPtr.Zero)
                        {
                            Data* data = (Data*)int_ptr;
                            if (data->IsValid)
                            {
                                Data.Destroy(*data);
                            }
                        }
                    }
                }
            }
        }*/
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
        private class Multi<Args> where Args : EventArgs
        {
            private readonly List<Args> argsList = new List<Args>();

            public void Add(Args args) => argsList.Add(args);
            public bool Remove(Args args) => argsList.Remove(args);
            public int RemoveWhere(Predicate<Args> predicate) => argsList.RemoveAll(predicate);
            public void Clear() => argsList.Clear();
            public bool Any(Func<Args, bool> predicate) => argsList.Any(predicate);
        }
    }
    public abstract class Engine
    {
        public class EngineWindow : RenderWindow
        {
            public static Vector2u WindowDimensions => MainEngine.Settings.WindowDimensions;
            public static Collision.AABB GetWindowBoundingBox => new Collision.AABB(0, 0, MainEngine.Settings.WindowDimensions.X, MainEngine.Settings.WindowDimensions.Y);
            public EngineWindow(VideoMode mode, string name, uint frame_rate) : base(mode, name)
            {
                SetFramerateLimit(frame_rate);
                this.Closed += OnQuit;
                this.KeyPressed += OnKeyPress;
                this.KeyReleased += OnKeyRelease;
                this.MouseButtonPressed += OnMousePress;
                this.MouseButtonReleased += OnMouseRelease;
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
        public virtual EngineSettings Settings => new EngineSettings(4, 1024, 1024, 10, new Vector2u(800, 600), "Window", false, false);
        private void GameLoop() // might make "public virtual"
        {
            var delta = CTime.DeltaTime;
            for (int i = 0; i < Collection.Subsystems.Count; i++)
            {
                var subsystem = Collection.Subsystems[i];
                if (subsystem.IsEnabled)
                {
#if DEBUG_TIMINGS
                    var time = DateTime.Now;
                    subsystem.Update(delta);
                    var now = DateTime.Now;
                    Console.WriteLine(subsystem.Name + ": " + ( now - time ).TotalMilliseconds);
#else
                    subsystem.Update(delta);
#endif
                }
            }
            Thread.Sleep((int)( CTime.DeltaTime * 1000 ));
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
                MainEngine = (Engine)Activator.CreateInstance(engineType);

                Entry(MainEngine.Settings.MainTableSize, MainEngine.Settings.ObjectTableSize, MainEngine.Settings.DataTableSize, MainEngine.Settings.TextureTableSize);
                Collection.AddNewSubsystem<RenderSubsystem>();
                Collection.AddNewSubsystem<UISubsystem>();

                if (MainEngine.Settings.EnablePhysics)
                {
                    Subsystem.AddNewDataType<PhysicsBody>();
                    Collection.AddNewSubsystem<PhysicsSubsystem>();
                }
                if (MainEngine.Settings.EnableCollisions)
                    Subsystem.AddNewDataType<Collider>();

                MainEngine.Initialise();

                bool foundCollisionSubsystem = Collection.Subsystems.Any(x => x.GetType().IsSubclassOf(typeof(CollisionSubsystem)));

                if (MainEngine.Settings.EnableCollisions && !foundCollisionSubsystem)
                    throw new Exception("Please provide implementation of collision subsystem.");
                if (!MainEngine.Settings.EnableCollisions && foundCollisionSubsystem)
                    throw new Exception("Cannot initialise collision subsystem if collision is disabled.");

                Subsystem.SystemFlags.SetFlags();

                foreach (var item in Collection.Subsystems)
                    if (item.IsEnabled)
                        item.Startup();

                while (MainEngine.ThisWindow is null && CTime.DeltaTime == 0f)
                    ;
                while (MainWindow.IsOpen)
                {
                    MainEngine.GameLoop();
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
        public uint DesiredFrameRate;
        public EngineSettings(int mSize, int otSize, int dtSize, int texSize, Vector2u window_dimensions, string name, bool enable_physics, bool enable_collisions, uint desiredFrameRate = 60)
        {
            MainTableSize = mSize;
            ObjectTableSize = otSize;
            DataTableSize = dtSize;
            TextureTableSize = texSize;
            WindowDimensions = window_dimensions;
            WindowName = name;
            EnablePhysics = enable_physics;
            EnableCollisions = enable_collisions;
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
        public abstract void Update(float deltaSeconds);
        public virtual void Startup() { }
    }

    public sealed class RenderSubsystem : Subsystem
    {
        private Thread RenderThread = null;
        public override void Startup()
        {
            IsEnabled = false;
            RenderThread = new Thread(() => specialCaseUpdate());
            RenderThread.Start();
        }
        public override void Update(float deltaSeconds) { }
        
        internal void specialCaseUpdate()
        {

            Engine.MainEngine.ThisWindow = new Engine.EngineWindow(new VideoMode(Engine.MainEngine.Settings.WindowDimensions), Engine.MainEngine.Settings.WindowName, Engine.MainEngine.Settings.DesiredFrameRate);

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
#if DEBUG_TIMINGS
                Console.WriteLine(Name + ": " + ( CTime.DeltaTime * 1000 ).ToString());
#endif
            }
        }
        
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

                body.Velocity += Constants.Gravity * deltaSeconds;

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
                    throw new ArgumentOutOfRangeException(nameof(index));
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
    }
    public static class GraphicsExtensions
    {
        public static Vector2f GetAbsoluteFromAnchor(this Anchor anchor)
        {
            var windowDimensions = Library.Engine.MainEngine.Settings.WindowDimensions;
            return anchor switch
            {
                Anchor.TOP_LEFT => new Vector2f(),
                Anchor.TOP_RIGHT => new Vector2f(windowDimensions.X, 0),
                Anchor.BOTTOM_LEFT => new Vector2f(0, windowDimensions.Y),
                Anchor.BOTTOM_RIGHT => new Vector2f(windowDimensions.X, windowDimensions.Y),
                _ => throw new Exception("Invalid Anchor."),
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
        internal bool ShouldDraw;
        internal Vertex4 Vertices;
        internal IntPtr TexturePtr;
        internal TextureEntry* Entry => (TextureEntry*)TexturePtr;
        public Texture(string filename, bool shouldDraw) : this(filename)
        {
            ShouldDraw = shouldDraw;
        }
        public Texture(string filename)
        {
            TexturePtr = Textures.TryAddTexture(filename);
            Vertices = new Vertex4();
            ShouldDraw = true;
            UpdateTexture(TexturePtr);
        }
        public void UpdateTexture(IntPtr texture)
        {
            if (texture != IntPtr.Zero)
            {
                this.TexturePtr = texture;

                var size = Entry->GetSize();
                Entry->TextureRect = new IntRect(0, 0, Convert.ToInt32(size.X), Convert.ToInt32(size.Y));

                var left = Convert.ToSingle(Entry->TextureRect.Left);
                var right = left + Entry->TextureRect.Width;
                var top = Convert.ToSingle(Entry->TextureRect.Top);
                var bottom = top + Entry->TextureRect.Height;

                Vertices.Vertex0 = new Vertex(new Vector2f(0, 0), Color.White, new Vector2f(left, top));
                Vertices.Vertex1 = new Vertex(new Vector2f(0, size.Y), Color.White, new Vector2f(left, bottom));
                Vertices.Vertex2 = new Vertex(new Vector2f(size.X, 0), Color.White, new Vector2f(right, top));
                Vertices.Vertex3 = new Vertex(new Vector2f(size.X, size.Y), Color.White, new Vector2f(right, bottom));
            }
        }
        public Texture ReturnNewTextureWithRandomColor()
        {
            var texture = this;
            texture.RandomiseColor();
            return texture;
        }
        public Texture CalculateShouldDraw(AABB _this, AABB other)
        {
            var texture = this;
            texture.ShouldDraw = other.Overlaps(_this);
            return texture;
        }
        public void RandomiseColor()
        {
            var color = new Color((byte)Next(0, 255), (byte)Next(0, 255), (byte)Next(0, 255));
            SetColor(color);
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
        public Vector2u GetSize => Entry->GetSize();
        public Color GetColor()
        {
            return Vertices.Vertex0.Color;
        }
        public void Draw(RenderTarget target, RenderStates states)
        {
            //if (Thread.CurrentThread != ProgenitorThread)
            //    throw new Exception("Multi-threaded drawing detected!");
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
                    throw new ArgumentOutOfRangeException(nameof(index));
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
                    throw new ArgumentOutOfRangeException(nameof(index));
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
    public struct Animation : IComponentData
    {
        public uint FrameCount;
        public IntPtr Frames;
    }
}
namespace ECS.GarbageCollection
{
    using static CAllocation;
    using static UnmanagedCSharp;
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
        // erase large blocks of dead space in table --- later optimisation

    }
}