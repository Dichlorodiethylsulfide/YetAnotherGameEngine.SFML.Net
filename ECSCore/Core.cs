using System;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using System.Runtime.InteropServices;

using SFML.Window;
using SFML.Graphics;
using SFML.System;

using ECS.Collision;
using ECS.Graphics;
using ECS.Physics;

namespace ECS 
{
    using static CAllocation;
    using static Debug;
    using static UnmanagedCSharp;
    public interface IComponentData { }
    public static class Debug
    {
        /*
        public static void AssertValidArgumentIndex(int index, int maxSize)
        {
            if (index < 0 || index >= maxSize)
                throw new ArgumentException();
        }
        */
    }
    public struct CObject : IEquatable<CObject>, IDisposable
    {
        private static int incrementer = 1;
        public static readonly CObject Null = new CObject();

        internal int Index;
        internal IntPtr MetaPtr;
        public int HasDataOf<T>() where T : unmanaged => this.HasDataOf(typeof(T).GetHashCode());
        internal unsafe int HasDataOf(int type) // use 'out' for multiple type checks?
        {
            if(MetaPtr != IntPtr.Zero)
                return ( (MetaCObject*)MetaPtr )->HasDataOf(type);
            return -1;
        }
        public int ID { get; private set; }
        public void AddData<T>(T data) where T : unmanaged
        {
            if(HasDataOf<T>() == -1)
            {
                AddObjectData(this, data);
            }
        }
        public T GetData<T>() where T : unmanaged
        {
            if(HasDataOf<T>() is int _int && _int != -1)
            {
                unsafe
                {
                    return ( (MetaCObject*)MetaPtr )->GetRef(_int)->GetInternalData<T>();
                }
            }
            throw new NullReferenceException();
        }
        internal unsafe T* GetDataPointer<T>() where T : unmanaged
        {
            if (HasDataOf<T>() is int _int && _int != -1)
            {
                unsafe
                {
                    return ( (MetaCObject*)MetaPtr )->GetRef(_int)->ExposeData<T>();
                }
            }
            throw new NullReferenceException();
        }
        public bool IsNull() => this.ID == 0;
        public override int GetHashCode() => this.ID;
        public bool Equals(CObject other) => this.ID == other.ID;
        public void Dispose()
        {
            Destroy(this);
        }
        internal void InternalDispose()
        {
            unsafe
            {
                ( (MetaCObject*)MetaPtr )->Dispose();
            }
            Free(MetaPtr);
        }
        public static void Destroy(CObject obj)
        {
            unsafe
            {
                ObjectTablePtr->RemoveObjectAtIndex(obj.Index);
            }
            obj.MetaPtr = IntPtr.Zero;
            obj.Index = -1;
            obj.ID = 0;
        }
        public static void Destroy(int index) // memset block will return 0 for index and delete the object at the 0th position, not good
        {
            unsafe
            {
                Destroy(ObjectTablePtr->GetObjectAtIndex(index));
            }
        }

        public static implicit operator bool(CObject obj) => !obj.IsNull();
        internal static CObject New(int index)
        {
            var obj = new CObject
            {
                ID = incrementer++,
                Index = index,
                MetaPtr = MetaCObject.New(),
            };
            return obj;
        }
        public static CObject New() => AddNewObject();
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

        public unsafe static int SizeOf<T>() where T : unmanaged => sizeof(T);

        
    }
    public static unsafe class UnmanagedCSharp
    {
        private const int DataTableStartIndex = 3; // Object Table at 0, Texture table will be 1, collisions is 2, should be 3
        private static int DefaultDataTableSize = 0;
        private static IntPtr Table = IntPtr.Zero;
        internal static LookupTable* TablePtr => (LookupTable*)Table;
        internal static ObjectTable* ObjectTablePtr => (ObjectTable*)*&TablePtr->Value0;
        internal static TextureTable* TextureTablePtr => (TextureTable*)*((&TablePtr->Value0) + 1);
        internal static AABBTree* Tree => (AABBTree*)*((&TablePtr->Value0) + 2);

        internal static Thread ProgenitorThread = null;

        private static RefObjectTable refObjects = default;
        public static RefObjectTable Entities => refObjects;


        internal static DataTable* GetDataTable(int index)
        {
            if (index < DataTableStartIndex || index >= TablePtr->Entries) // skip object table
                throw new NullReferenceException(nameof(index));
            return (DataTable*)( *( &TablePtr->Value0 + index ) );
        }
        internal static DataTable* GetDataTableOf(int type)
        {
            for(int i = DataTableStartIndex; i < TablePtr->Entries; i++)
            {
                var table = GetDataTable(i);
                if(table->DataType == type)
                    return table;
            }
            throw new ArgumentException(nameof(type));
        }

        public static void Entry(int mainSize = 4, int objectSize = 1024, int defaultDataSize = 1024, int textureSize = 10)
        {
            ProgenitorThread = Thread.CurrentThread;
            CreateMainTable(mainSize + DataTableStartIndex);
            CreateObjectTable(objectSize);
            CreateTextureTable(textureSize);
            CreateTree(objectSize);
            refObjects = RefObjectTable.New();
            DefaultDataTableSize = defaultDataSize;
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
            for(int i = DataTableStartIndex; i < TablePtr->Entries; i++)
            {
                if(GetDataTable(i)->DataType == hash)
                {
                    throw new ArgumentException("Type already exists.");
                }
            }
            CreateDataTable<T>(DefaultDataTableSize);
        }
        private static void CreateTree(int size = 1024)
        {
            var bytes = sizeof(AABBTree);
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var tree = (AABBTree*)entry;
            *tree = new AABBTree(size);//AABBTree.New(null);
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
            var bytes = ( sizeof(int) * 3 ) + ( size * sizeof(CObject) );
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var table = (ObjectTable*)entry;
            *table = new ObjectTable();
            table->Entries = 0;
            table->MaxSize = size;
            table->AllocatedBytes = bytes;
            TablePtr->SetNext(entry);
        }
        private static void AddNewObject(ObjectTable* table, CObject cObject)
        {
            if (table->Entries >= table->MaxSize)
            {
                throw new Exception("Not enough space in table");
            }
            table->SetNext(cObject);
        }
        internal static CObject AddNewObject()
        {
            var obj = CObject.New(ObjectTablePtr->Entries);
            AddNewObject(ObjectTablePtr, obj);
            return obj;
        }
        internal static void AddObjectData<T>(CObject cObject, T data) where T : unmanaged
        {
            var type = typeof(T).GetHashCode();
            AddNewData(GetDataTableOf(type), cObject, type, data);
        }
        internal static T GetObjectData<T>(CObject cObject) where T : unmanaged
        {
            var type = typeof(T).GetHashCode();
            return GetDataTableOf(type)->GetDataAtIndex(cObject.Index).GetInternalData<T>();
        }
        internal static T* GetObjectDataPointer<T>(CObject cObject) where T : unmanaged
        {
            var type = typeof(T).GetHashCode();
            return GetDataTableOf(type)->GetDataAtIndex(cObject.Index).ExposeData<T>();
        }
        private static void AddNewData<T>(DataTable* table, CObject cObject, int type, T data) where T : unmanaged
        {
            if (table->DataType != type)
            {
                throw new Exception("Data type mismatch.");
            }
            if (table->Entries >= table->MaxSize)
            {
                throw new Exception("Not enough space in table");
            }
            var entry = new Data
            {
                DataType = type,
                IsInitialised = true,
                DataSize = sizeof(T),
                ObjectPtr = ObjectTablePtr->GetObjectPointerAtIndex(cObject.Index) // check this just in case, it appears to work so far
            };
            entry.DataPtr = Malloc(entry.DataSize);
            MemSet(entry.DataPtr, 0, entry.DataSize);
            *(T*)entry.DataPtr = data;

            //if (cObject.Index != table->Entries)
            //    Console.WriteLine("Type: " + type + " is at a different position than the object it is assigned to.\nObject at: " + cObject.Index + "\nData at: " + table->Entries);

            table->SetNext(entry);
            ( (MetaCObject*)cObject.MetaPtr )->AddRef(table->GetLast());
        }
        private static void CreateDataTable<T>(int size = 1024) where T : unmanaged
        {
            var bytes = ( sizeof(int) * 4 ) + ( size * sizeof(Data) );
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var table = (DataTable*)entry;
            *table = new DataTable();
            table->Entries = 0;
            table->MaxSize = size;
            table->AllocatedBytes = bytes;
            table->DataType = typeof(T).GetHashCode();
            TablePtr->SetNext(entry);
        }
        private static void CreateTextureTable(int size = 1024)
        {
            var bytes = ( sizeof(int) * 3 ) + ( size * sizeof(TextureEntry) );
            var entry = Malloc(bytes);
            MemSet(entry, 0, bytes);
            var table = (TextureTable*)entry;
            *table = new TextureTable();
            table->Entries = 0;
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
            public int Entries;
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
            public void SetNext(CObject cObject) => SetObjectAtIndex(Entries++, cObject);

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
                    Entries--;
                }
            }
            public void IterateTable<T>(RefWithObject<T, CObject> action, bool multi_threaded = false) where T : unmanaged
            {
                var type = typeof(T).GetHashCode();
                var table = GetDataTableOf(type);
                var counter = 0;
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (cObject->HasDataOf(type) is int _int && _int != -1)
                        {
                            ( (MetaCObject*)cObject->MetaPtr )->GetRef(_int)->UseInternalData(action);
                            counter++;
                        }
                        else if (counter >= table->Entries)//(i > this.Entries)
                        {
                            // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                            return;
                        }
                    }
                }

            }
            public void IterateTable<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                var type = typeof(T).GetHashCode();
                var table = GetDataTableOf(type);
                var counter = 0;
                fixed(CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if(cObject->HasDataOf(type) is int _int && _int != -1)
                        {
                            ( (MetaCObject*)cObject->MetaPtr )->GetRef(_int)->UseInternalData(action);
                            counter++;
                        }
                        else if (counter >= table->Entries)//(i > this.Entries)
                        {
                            // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                            return;
                        }
                    }
                }
                
            }

            public void IterateTable<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                var type = typeof(T).GetHashCode();
                var type2 = typeof(U).GetHashCode();

                var table = GetDataTableOf(type);
                var table2 = GetDataTableOf(type2);

                var size = Math.Min(table->Entries, table2->Entries);

                var counter = 0;
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if((cObject->HasDataOf(type) is int _int && _int != -1) && ( cObject->HasDataOf(type2) is int _int2 && _int2 != -1 ))
                        {
                            action(ref *( (MetaCObject*)cObject->MetaPtr )->GetRef(_int)->ExposeData<T>(),
                                   ref *( (MetaCObject*)cObject->MetaPtr )->GetRef(_int2)->ExposeData<U>());
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

            public void IterateTable<T, U, V>(RefRefRef<T, U, V> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged where V : unmanaged
            {
                var type = typeof(T).GetHashCode();
                var type2 = typeof(U).GetHashCode();
                var type3 = typeof(V).GetHashCode();

                var table = GetDataTableOf(type);
                var table2 = GetDataTableOf(type2);
                var table3 = GetDataTableOf(type3);

                var size = Math.Min(table->Entries, table2->Entries);
                size = Math.Min(size, table3->Entries);

                var counter = 0;
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (( cObject->HasDataOf(type) is int _int && _int != -1 ) &&
                            ( cObject->HasDataOf(type2) is int _int2 && _int2 != -1 ) &&
                            ( cObject->HasDataOf(type3) is int _int3 && _int3 != -1 ))
                        {
                            action(ref *( (MetaCObject*)cObject->MetaPtr )->GetRef(_int)->ExposeData<T>(),
                                   ref *( (MetaCObject*)cObject->MetaPtr )->GetRef(_int2)->ExposeData<U>(),
                                   ref *( (MetaCObject*)cObject->MetaPtr )->GetRef(_int3)->ExposeData<V>());
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

            public List<Collision> CollisionQuery(bool multi_threaded = false)
            {
                var type = typeof(Transform).GetHashCode();
                var type2 = typeof(Collider).GetHashCode();

                var table = GetDataTableOf(type);
                var table2 = GetDataTableOf(type2);

                var size = Math.Min(table->Entries, table2->Entries);

                var counter = 0;

                List<Collision> Collisions = new List<Collision>();
                fixed (CObject* object_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if ((cObject->HasDataOf(type) is int _int && _int != -1 ) && ( cObject->HasDataOf(type2) is int _int2 && _int2 != -1 ))
                        {
                            Collider* data = ( (MetaCObject*)cObject->MetaPtr )->GetRef(_int2)->ExposeData<Collider>();
                            if(!data->IsInTree)
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
                    // Return collisions
                    counter = 0;
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var cObject = object_ptr + i;
                        if (( cObject->HasDataOf(type) is int _int && _int != -1 ) && ( cObject->HasDataOf(type2) is int _int2 && _int2 != -1 ))
                        {
                            if (Tree->QueryOverlaps(cObject) is List<CObject> list && list.Count > 0)
                                for (int j = 0; j < list.Count; j++)
                                    Collisions.Add(new Collision(cObject, list[j]));
                            counter++;
                        }
                        else if (counter >= size)
                        {
                            break;
                        }
                    }
                }
                return Collisions;
            }
          
        }
        public struct Collision
        {
            public CObject This;
            public CObject Other;
            internal Collision(CObject* _this, CObject _other)
            {
                This = *_this;
                Other = _other;
            }
        }
        public struct RefObjectTable
        {
            private IntPtr table;

            public void Iterate<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            public void IterateWithObject<T>(RefWithObject<T, CObject> action, bool multi_threaded = false) where T : unmanaged
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
            public List<Collision> CollisionQuery()
            {
                return ( (ObjectTable*)table )->CollisionQuery();
            }
            public static RefObjectTable New()
            {
                var refTable = new RefObjectTable()
                {
                    table = (IntPtr)ObjectTablePtr,
                };
                return refTable;
            }
        }
        /*
        public struct RefDataTable
        {
            private int type;
            private IntPtr table;

            public T GetData<T>(int index) where T : unmanaged
            {
                var _type = typeof(T).GetHashCode();
                if(_type != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }
                return ( (DataTable*)table )->GetDataAtIndex(index).GetInternalData<T>();
            }

            public void IterateTable<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                if(typeof(T).GetHashCode() != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }
                ( (DataTable*)table )->IterateTable(action, multi_threaded);
            }
            public int IterateTable<T>(RefWithIteration<T> action, bool multi_threaded = false) where T : unmanaged
            {
                if (typeof(T).GetHashCode() != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }
                return ( (DataTable*)table )->IterateTable(action, multi_threaded);
            }
            public void IterateTable<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                if (typeof(T).GetHashCode() != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }
                ( (DataTable*)table )->IterateTable(action, multi_threaded);
            }

            private static RefDataTable New(Type type)
            {
                var _t = type.GetHashCode();
                var refTable = new RefDataTable()
                {
                    type = _t,
                    table = (IntPtr)GetDataTableOf(_t),
                };
                return refTable;
            }
            public static RefDataTable CreateFromUnknownType<U>()
            {
                var type = typeof(U);
                if (!type.IsValueType)
                {
                    throw new ArgumentException("Invalid generic type.");
                }
                return New(type);
            }
        }
        */
        public delegate void Ref<T0>(ref T0 data0);
        public delegate void RefWithObject<T0, O0>(ref T0 data0, ref O0 object0);
        //public delegate void RefWithIteration<T0>(ref T0 data0, int current_iteration, int max_iteration);
        public delegate void RefRef<T0, T1>(ref T0 data0, ref T1 data1);
        public delegate void RefRefRef<T0, T1, T2>(ref T0 data0, ref T1 data1, ref T2 data2);
        public delegate void In<T0>(in T0 data0);
        public delegate int Compare<T0>(in T0 data, in T0 otherData);
        internal struct DataTable
        {
            public int DataType;
            public int Entries;
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
                    Entries--;
                }
            }

            public void SetNext(Data data) => SetDataAtIndex(Entries++, data);
            public IntPtr GetLast() => GetDataPointerAtIndex(Entries - 1);

            /*
            public int IterateTable<T>(RefWithIteration<T> action, bool multi_threaded = false) where T : unmanaged
            {
                fixed (Data* data_ptr = &this.Value0)
                {
                    if (multi_threaded)
                    {
                        var table = GetDataTableOf(DataType);
                        var entries = this.Entries;
                        var count = 0;
                        Parallel.For(0, this.MaxSize, x =>
                        {
                            if (x > entries) // Same as below Debug
                            {
                                Interlocked.Increment(ref count);
                                return;
                            }
                            var data = table->GetDataAtIndex(x);
                            if (data.IsValid)
                            {
                                data.UseInternalData(action, x, entries);
                            }
                        });
                        return count;
                    }
                    else
                    {
                        for (int i = 0; i < this.MaxSize; i++)
                        {
                            var data = data_ptr + i;
                            if (data->IsValid)
                            {
                                data->UseInternalData(action, i, this.Entries);
                            }
                            else if (i > this.Entries)
                            {
                                // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                                return i;
                            }
                        }
                    }
                    return -1;
                }

            }

            public void IterateTable<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                fixed(Data* data_ptr = &this.Value0)
                {
                    if (multi_threaded)
                    {
                        var table = GetDataTableOf(DataType);
                        var entries = this.Entries;
                        Parallel.For(0, this.MaxSize, x =>
                        {
                            if (x > entries) // Same as below Debug
                                return;
                            var data = table->GetDataAtIndex(x);
                            if (data.IsValid)
                            {
                                data.UseInternalData(action);
                            }
                        });
                    }
                    else
                    {
                        for (int i = 0; i < this.MaxSize; i++)
                        {
                            var data = data_ptr + i;
                            if (data->IsValid)
                            {
                                data->UseInternalData(action);
                            }
                            else if (i > this.Entries)
                            {
                                // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                                break;
                            }
                        }
                    }
                }

            }

            public void IterateTable<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                if(multi_threaded)
                {
                    var table = GetDataTableOf(DataType);
                    var other_table = GetDataTableOf(typeof(U).GetHashCode());
                    var entries = this.Entries;
                    Parallel.For(0, this.MaxSize, i =>
                    {
                        if (i > entries)
                            return;
                        var data = table->GetDataAtIndex(i);
                        var data1 = other_table->GetDataAtIndex(i);
                        if (data.IsValid && data1.IsValid)
                        {
                            action(ref *(T*)data.DataPtr, ref *(U*)data1.DataPtr);
                        }
                    });
                }
                else
                {
                    var other_table = GetDataTableOf(typeof(U).GetHashCode());
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var data = this.GetDataAtIndex(i);
                        var data1 = other_table->GetDataAtIndex(i);
                        if (data.IsValid && data1.IsValid)
                        {
                            action(ref *(T*)data.DataPtr, ref *(U*)data1.DataPtr);
                        }
                        else if(i > this.Entries)
                        {
                            break;
                        }
                    }
                }
            }
            */
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
            public int Entries;
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

            public void SetNext(TextureEntry entry) => SetEntryAtIndex(Entries++, entry);
            public IntPtr GetLast() => GetEntryPointerAtIndex(Entries - 1);
            public IntPtr Find(string filename)
            {
                var hash = filename.GetHashCode();
                fixed(TextureEntry* ptr = &this.Value0)
                {
                    for(int i = 0; i < Entries; i++)
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
                if (Entries >= MaxSize)
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
        /*
        public struct TextureEntry
        {
            public String Name;
            public IntRect TextureRect;
            public IntPtr TexturePtr;

            public Vector2u GetSize() => SFML.Graphics.Texture.sfTexture_getSize(TexturePtr);
            public static IntPtr New(string filename)
            {
                if(TextureTablePtr->Entries >= TextureTablePtr->MaxSize)
                {
                    throw new Exception("Not enough space in table");
                }
                var entry = new TextureEntry()
                {
                    Name = String.New(filename),
                    TextureRect = new IntRect()
                };
                entry.TexturePtr = Texture.sfTexture_createFromFile(filename, ref entry.TextureRect);
                TextureTablePtr->SetNext(entry);
                return TextureTablePtr->GetLast();
            }
        }
        public struct String
        {
            public int Length;
            public int HashCode;
            public IntPtr Start;

            public static String New(string text)
            {
                var new_string = new String()
                {
                    Length = text.Length,
                    HashCode = text.GetHashCode()
                };
                new_string.Start = Malloc(text.Length);
                MemSet(new_string.Start, 0, text.Length);
                for(int i = 0; i < text.Length; i++)
                {
                    *( (char*)new_string.Start + i ) = text[i];
                }
                return new_string;
            }
            public override string ToString()
            {
                string new_string = string.Empty;
                for (int i = 0; i < Length; i++)
                {
                    new_string += *( (char*)Start + i );
                }
                return new_string;
            }

        }
        */
        internal struct Data : IDisposable
        {
            public static readonly Data Null = new Data();

            public bool IsInitialised;
            public int DataType;
            public int DataSize;
            public IntPtr DataPtr;
            public IntPtr ObjectPtr;
            public bool IsValid => IsInitialised && DataPtr != IntPtr.Zero && ObjectPtr != IntPtr.Zero;
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
            public void UseInternalData<T>(RefWithObject<T, CObject> action) where T : unmanaged => action(ref *(T*)this.DataPtr, ref *(CObject*)ObjectPtr);

            public void Dispose()
            {
                // Kill link to object --- clear object's meta data cache
                ObjectPtr = IntPtr.Zero;
                // Kill link to data --- clear data's data
                Free(DataPtr);
                DataPtr = IntPtr.Zero;
            }

            public static void Destroy(Data data)
            {
                GetDataTableOf(data.DataType)->RemoveDataAtIndex(( (CObject*)data.ObjectPtr )->Index);
            }
        }
        internal struct MetaCObject : IDisposable
        {
            public int Entries;
            public int MaxSize;
            public IntPtr DataPtr;

            public void Dispose()
            {
                // Kill link to data --- clear data's data
                fixed (IntPtr* ptr = &DataPtr)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        var int_ptr = *( ptr + i );
                        if(int_ptr != IntPtr.Zero)
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

            public void AddRef(IntPtr dataPtr)
            {
                if(Entries >= MaxSize)
                {
                    throw new Exception("Not enough space in meta object");
                }
                fixed (IntPtr* ptr = &DataPtr)
                {
                    *( ptr + Entries++ ) = dataPtr;
                }
            }
            public Data* GetRef(int index)
            {
                if(index < 0 || index >= Entries)
                    throw new ArgumentException(nameof(index));
                fixed (IntPtr* ptr = &DataPtr)
                {
                    return (Data*)*(ptr + index);
                }
            }
            public void RemoveRef(int index)
            {
                if (index < 0 || index >= Entries)
                    throw new ArgumentException(nameof(index));
                fixed (IntPtr* ptr = &DataPtr)
                {
                    Data.Destroy(*( (Data*)ptr + index ));//->Dispose();
                    for(int i = index; i < this.MaxSize; i++)
                    {
                        ptr[i] = ptr[i + 1];
                    }
                    MemSet(*( ptr + this.MaxSize - 1 ), 0, sizeof(IntPtr));
                    Entries--;
                }
            }
            public int HasDataOf(int type)
            {
                fixed (IntPtr* ptr = &DataPtr)
                {
                    for (int i = 0; i < Entries; i++)
                    {
                        if (type == ((Data*)*(ptr + i))->DataType)
                        {
                            return i;
                        }
                    }
                }
                return -1;
            }

            public static IntPtr New(int size = 8)
            {
                var bytes = ( sizeof(int) * 2 ) + ( sizeof(IntPtr) * size );
                var ptr = Malloc(bytes);
                MemSet(ptr, 0, bytes);
                var metaCObject = new MetaCObject
                {
                    Entries = 0,
                    MaxSize = size
                };
                *(MetaCObject*)ptr = metaCObject;
                return ptr;
            }

        }
    }
}
namespace ECS.Graphics
{
    using static RenderWindow;
    using static UnmanagedCSharp;
    public struct Transform : IComponentData
    {
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
                return myPosition;
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
                    float tx = -myOrigin.X * sxc - myOrigin.Y * sys + myPosition.X;
                    float ty = myOrigin.X * sxs - myOrigin.Y * syc + myPosition.Y;

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
        public Transform(float x, float y, uint height, uint width) : this(new Vector2f(x, y), new Vector2u(height, width)) { }
        public Transform(Vector2f position, Vector2u size)
        {
            mySize = size;
            //myBounds = new FloatRect(new Vector2f(), (Vector2f)size);
            myOrigin = (Vector2f)size / 2;
            myPosition = position;
            myRotation = 0;
            myScale = new Vector2f(1, 1);
            myTransformNeedUpdate = true;
            myInverseNeedUpdate = true;
            myTransform = default;
            myInverseTransform = default;
        }
        //public FloatRect GetGlobalBounds() => SFMLTransform.TransformRect(myBounds);
        //public FloatRect GetLocalBounds() => myBounds;
        //private FloatRect myBounds;

        public AABB BoundingBox => new AABB(myPosition.X, myPosition.Y, myPosition.X + mySize.X, myPosition.Y + mySize.Y);
        private Vector2u mySize;
        private Vector2f myOrigin;// = new Vector2f(0, 0);
        private Vector2f myPosition;// = new Vector2f(0, 0);
        private float myRotation;// = 0;
        private Vector2f myScale;// = new Vector2f(1, 1);
        private SFMLTransform myTransform;
        private SFMLTransform myInverseTransform;
        private bool myTransformNeedUpdate;// = true;
        private bool myInverseNeedUpdate;// = true;
    }
    public unsafe struct Texture : Drawable, IComponentData
    {
        internal Vertex4 Vertices;
        internal IntPtr TexturePtr;
        internal TextureEntry* Entry => (TextureEntry*)TexturePtr;
        public Texture(string filename)
        {
            TexturePtr = TryAddTexture(filename);
            Vertices = new Vertex4();
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
        private static readonly Random rnd = new Random();
        public void RandomiseColor()
        {
            var color = new Color((byte)rnd.Next(0, 255), (byte)rnd.Next(0, 255), (byte)rnd.Next(0, 255));
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
            if (Thread.CurrentThread != ProgenitorThread)
                throw new Exception("Multi-threaded drawing detected!");
            states.CTexture = Entry->TexturePtr;
            ( (RenderWindow)target ).Draw(Vertices, PrimitiveType.TriangleStrip, states);;
        }
    }
}
namespace ECS.Maths
{
    public static class Constants
    {
        public static readonly Vector2f Gravity = new Vector2f(0, 9.80665f);
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
        public static Vector2f Min(this FloatRect box) => new Vector2f(Math.Min(box.Left, box.Left + box.Width), Math.Min(box.Top, box.Top + box.Height));
        public static Vector2f Max(this FloatRect box) => new Vector2f(Math.Max(box.Left, box.Left + box.Width), Math.Max(box.Top, box.Top + box.Height));

        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
    }

}
namespace ECS.Collections
{
    using static CAllocation;
    public static unsafe class Generic
    {
        public struct LocalArray<T> : IDisposable where T : unmanaged
        {
            public int Entries;
            public int MaxSize;
            public int AllocatedBytes;
            private IntPtr Items;
            public LocalArray(int size)
            {
                Entries = 0;
                MaxSize = size;
                AllocatedBytes = ( sizeof(T) * size );
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
                IntPtr new_block = Malloc(sizeof(T) * size);
                MemSet(new_block, 0, sizeof(T) * size);
                MemMove(new_block, Items, sizeof(T) * MaxSize);
                Free(Items);
                Items = new_block;
                MaxSize = size;
                AllocatedBytes = sizeof(T) * size;
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

        private LocalDictionary<int, int> map;
        private LocalArray<AABBNode> nodes;
        private int rootNodeIndex;
        private int allocatedNodeCount;
        private int nextFreeNodeIndex;
        private int nodeCapacity;
        private int growthSize;

        public static AABBTree New(AABBTree* original)
        {
            var size = 1;
            if (original != null)
            {
                size = original->nodes.MaxSize;
                original->Dispose();
            }
            return new AABBTree(size);
        }

        public AABBTree(int start_size)
        {
            rootNodeIndex = AABBNode.NULL_NODE;
            allocatedNodeCount = 0;
            nextFreeNodeIndex = 0;
            nodeCapacity = start_size;
            growthSize = start_size;
            nodes = new LocalArray<AABBNode>(start_size);
            map = new LocalDictionary<int, int>(start_size);
            for (int nodeIndex = 0; nodeIndex < start_size; nodeIndex++)
            {
                AABBNode node = AABBNode.New();
                node.NextNodeIndex = nodeIndex + 1;
                nodes.Add(node);
            }
            nodes.ReadPointerAt(start_size - 1)->NextNodeIndex = AABBNode.NULL_NODE;
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

            node->AssociatedAABB = cObject->GetDataPointer<Transform>()->BoundingBox;
            node->ObjectPointer = cObject;

            insertLeaf(nodeIndex);
            map[cObject->ID] = nodeIndex;
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
            updateLeaf(nodeIndex, cObject->GetDataPointer<Transform>()->BoundingBox);
        }

        public List<CObject> QueryOverlaps(CObject* cObject)
        {
            List<CObject> overlaps = new List<CObject>();
            Stack<int> stack = new Stack<int>();
            AABB testAabb = cObject->GetDataPointer<Transform>()->BoundingBox;
            stack.Push(rootNodeIndex);
            while (stack.Count != 0)
            {
                int nodeIndex = stack.Pop();

                if (nodeIndex == AABBNode.NULL_NODE)
                    continue;

                AABBNode* node = nodes.ReadPointerAt(nodeIndex);

                if (node->AssociatedAABB.Overlaps(testAabb))
                {
                    if (node->IsLeaf() && !node->ObjectPointer->Equals(*cObject))
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

        public void Dispose()
        {
            map.Dispose();
            nodes.Dispose();
        }
    }
}