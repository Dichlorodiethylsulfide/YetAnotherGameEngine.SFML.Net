using System;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using System.Runtime.InteropServices;

namespace ECS
{
    public interface IComponentData { }
    public class ReadOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();
        public ReadOnlyDictionary(Dictionary<TKey, TValue> dictionary) => _dictionary = dictionary;
        public TValue this[TKey key] { get => _dictionary[key]; set => _dictionary[key] = value; }
        public ICollection<TKey> Keys => _dictionary.Keys;
        public ICollection<TValue> Values => _dictionary.Values;
        public int Count => _dictionary.Count;
        public bool IsReadOnly => true;

        #region Readonly: Not Allowed
        public void Add(TKey key, TValue value) => throw new NotImplementedException();
        public void Add(KeyValuePair<TKey, TValue> item) => throw new NotImplementedException();
        public void Clear() => throw new NotImplementedException();
        public bool Remove(TKey key) => throw new NotImplementedException();
        public bool Remove(KeyValuePair<TKey, TValue> item) => throw new NotImplementedException();
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => throw new NotImplementedException();
        #endregion Readonly: Not Allowed

        public bool Contains(KeyValuePair<TKey, TValue> item) => _dictionary.Contains(item);
        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dictionary.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public class ReadOnlyList<T> : IList<T>
    {
        private List<T> _list = new List<T>();
        public ReadOnlyList(List<T> list) => _list = list;
        public T this[int index] { get => _list[index]; set => _list[index] = value; }
        public int Count => _list.Count;
        public bool IsReadOnly => true;

        #region Readonly: Not Allowed
        public void Add(T item) => throw new NotImplementedException();
        public void Clear() => throw new NotImplementedException();
        public void CopyTo(T[] array, int arrayIndex) => throw new NotImplementedException();
        public void Insert(int index, T item) => throw new NotImplementedException();
        public bool Remove(T item) => throw new NotImplementedException();
        public void RemoveAt(int index) => throw new NotImplementedException();
        #endregion Readonly: Not Allowed

        public bool Contains(T item) => _list.Contains(item);
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        public int IndexOf(T item) => _list.IndexOf(item);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class ReadOnlyComponentDataList
    {
        private readonly ComponentDataList _list = null;
        private ReadOnlyComponentDataList() { }
        public ReadOnlyComponentDataList(ComponentDataList list) { _list = list; }
        public ObjectDataPair this[int index]
        {
            get => _list[index];
        }
        public int Capacity => _list.Capacity;
    }
    public class ComponentDataList
    {
        private readonly Dictionary<CObject, int> CObjectIndexes = new Dictionary<CObject, int>();
        private ObjectDataPair[] componentDataPairs = new ObjectDataPair[1];
        //public int Count => throw new NotImplementedException();
        //public bool IsReadOnly => throw new NotImplementedException();
        //public void Add(T item) => throw new NotImplementedException();
        //public void Clear() => throw new NotImplementedException();
        //public bool Contains(T item) => throw new NotImplementedException();
        //public int IndexOf(T item) => throw new NotImplementedException();
        //public void Insert(int index, T item) => throw new NotImplementedException();
        //public bool Remove(T item) => throw new NotImplementedException();
        //public void RemoveAt(int index) => throw new NotImplementedException();

        public ObjectDataPair this[int index]
        {
            get => componentDataPairs[index];
            set => componentDataPairs[index] = value;
        }

        public ComponentData this[CObject cObject]
        {
            get => componentDataPairs[CObjectIndexes[cObject]].Value;
            //set => componentDataPairs[CObjectIndexes[cObject]].Value = value;
            set
            {
                unsafe
                {
                    fixed (ComponentData* ptr = &componentDataPairs[CObjectIndexes[cObject]].Value)
                    {
                        if(ptr->DataPointer != value.DataPointer)
                        {
                            ptr->Dispose();
                        }
                        *ptr = value;
                    }
                }
            }
        }
        public int Capacity => componentDataPairs.Length;
        public bool IsReadOnly => false;

        private int next = 0;
        public void Add(CObject cObject, ComponentData data)
        {
            if(!CObjectIndexes.ContainsKey(cObject))
            {
                CObjectIndexes.Add(cObject, next);
                if(next >= componentDataPairs.Length)
                {
                    var new_arr = new ObjectDataPair[componentDataPairs.Length * 2];
                    Array.Copy(componentDataPairs, new_arr, componentDataPairs.Length);
                    componentDataPairs = new_arr;
                }
                componentDataPairs[next++] = new ObjectDataPair(cObject, data);
            }
        }
        public bool Contains(CObject cObject) => this.CObjectIndexes.ContainsKey(cObject);
        public void Remove(CObject cObject)
        {
            if(CObjectIndexes.TryGetValue(cObject, out var index))
            {
                CObjectIndexes.Remove(cObject);
                componentDataPairs[index].Value.Dispose();
                componentDataPairs[index].Value = new ComponentData();
            }
        }
    }
    public struct CObject : IEquatable<CObject>
    {
        private static int incrementer = 1;
        public static readonly CObject Null = new CObject();

        internal int Index;
        internal IntPtr MetaPtr;
        internal unsafe bool HasDataOf<T>() where T : unmanaged => ( (UnmanagedCSharp.MetaCObject*)MetaPtr )->HasDataOf(typeof(T).GetHashCode());
        public int ID { get; private set; }
        public void AddData<T>(T data) where T : unmanaged
        {
            if(!HasDataOf<T>())
            {
                UnmanagedCSharp.AddObjectData(this, data);
            }
        }
        public T GetData<T>() where T : unmanaged => UnmanagedCSharp.GetObjectData<T>(this);
        public bool IsNull() => this.ID == 0;
        public override int GetHashCode() => this.ID;
        public bool Equals(CObject other) => this.ID == other.ID;
        public static implicit operator bool(CObject obj) => !obj.IsNull();
        internal static CObject New(int index)
        {
            var obj = new CObject
            {
                ID = incrementer++,
                Index = index,
                MetaPtr = UnmanagedCSharp.AllocateMetaCObject()
            };
            return obj;
        }
        public static CObject New() => UnmanagedCSharp.AddNewObject();
    }
    public struct ObjectDataPair
    {
        public CObject Key;
        public ComponentData Value;
        public ObjectDataPair(CObject key, ComponentData value)
        {
            Key = key;
            Value = value;
        }
    }
    /*
    public struct ComponentData<T> : IDisposable, IComponentData where T : unmanaged
    {
        public bool IsInitialised { get; private set; }
        internal int DataType;
        internal IntPtr DataPointer;
        public T ReadData()
        {
            if (DataType != 0)
            {
                unsafe
                {
                    return *(T*)DataPointer;
                }
            }
            return default;
            //throw new Exception("Read Data error: data types did not match.");
        }
        public void WriteData(T data)
        {;
            if (DataType != 0)
            {
                unsafe
                {
                    *(T*)DataPointer = data;
                }
                return;
            }
            //throw new Exception("Write Data error: data types did not match.");
        }
        public void Dispose()
        {
            DataType = 0;
            IsInitialised = false;
            if (DataPointer != IntPtr.Zero)
            {
                CAllocation.Free(DataPointer);
            }
        }
        public static ComponentData<T> New()
        {
            var data = new ComponentData<T>();
            var type = typeof(T);
            if (!typeof(IComponentData).IsAssignableFrom(type))
            {
                throw new Exception("Data type error");
            }
            data.DataType = type.GetHashCode();
            data.IsInitialised = true;
            unsafe
            {
                data.DataPointer = CAllocation.Malloc(sizeof(T));
                CAllocation.MemSet(data.DataPointer, 0, sizeof(T));
            }
            return data;
        }
        public static ComponentData<T> New(ComponentData comp_data)
        {
            var data = new ComponentData<T>();
            var type = typeof(T);
            if (!typeof(IComponentData).IsAssignableFrom(type))
            {
                throw new Exception("Data type error");
            }
            if(comp_data.DataType == type.GetHashCode())
            {
                data.DataType = comp_data.DataType;
                data.IsInitialised = true;
                data.DataPointer = comp_data.DataPointer;
                return data;
            }
            return default;
        }
    }
    */
    public struct ComponentData : IDisposable, IComponentData
    {
        public bool IsInitialised { get; private set; }
        internal int DataType;
        internal IntPtr DataPointer;
        public T ReadData<T>() where T : unmanaged
        {
            var hash = typeof(T).GetHashCode();
            if(DataType != 0 && DataType == hash)
            {
                unsafe
                {
                    return *(T*)DataPointer;
                }
            }
            return default;
            //throw new Exception("Read Data error: data types did not match.");
        }
        public void WriteData<T>(T data) where T : unmanaged
        {
            var hash = typeof(T).GetHashCode();
            if (DataType != 0 && DataType == hash)
            {
                unsafe
                {
                    *(T*)DataPointer = data;
                }
                return;
            }
            
            //throw new Exception("Write Data error: data types did not match.");
        }
        public void Dispose()
        {
            DataType = 0;
            IsInitialised = false;
            if(DataPointer != IntPtr.Zero)
            {
                CAllocation.Free(DataPointer);
            }
        }
        public static ComponentData New<T>() where T : unmanaged
        {
            var data = new ComponentData();
            var type = typeof(T);
            if(!typeof(IComponentData).IsAssignableFrom(type))
            {
                throw new Exception("Data type error");
            }
            data.DataType = type.GetHashCode();
            data.IsInitialised = true;
            unsafe
            {
                data.DataPointer = CAllocation.Malloc(sizeof(T));
                CAllocation.MemSet(data.DataPointer, 0, sizeof(T));
            }
            return data;
        }
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
        private static int DefaultDataTableSize = 0;
        private static IntPtr Table = IntPtr.Zero;

        internal static LookupTable* TablePtr => (LookupTable*)Table;
        internal static ObjectTable* ObjectTablePtr => (ObjectTable*)(*(IntPtr*)&TablePtr->Value0);
        //internal static DataTable* DataTablePtr => (DataTable*)(*((IntPtr*)&TablePtr->Value0 + 1));
        internal static DataTable* GetDataTable(int index)
        {
            if (index < 1 || index >= TablePtr->Entries) // skip object table
                throw new NullReferenceException(nameof(index));
            return (DataTable*)( *( (IntPtr*)&TablePtr->Value0 + index ) );
        }
        internal static DataTable* GetDataTableOf(int type)
        {
            for(int i = 1; i < TablePtr->Entries; i++)
            {
                var table = GetDataTable(i);
                if(table->DataType == type)
                    return table;
            }
            throw new ArgumentException(nameof(type));
        }

        public static void Entry(int mainSize = 4, int objectSize = 1024, int defaultDataSize = 1024)
        {
            CreateMainTable(mainSize + 1);
            CreateObjectTable(objectSize);

            DefaultDataTableSize = defaultDataSize;
        }

        public static void AddNewDataType<T>() where T : unmanaged
        {
            var type = typeof(T);
            if(!typeof(IComponentData).IsAssignableFrom(type) || !type.IsValueType)
            {
                throw new ArgumentException("Invalid generic type.");
            }
            var hash = type.GetHashCode();
            for(int i = 1; i < TablePtr->Entries; i++)
            {
                if(GetDataTable(i)->DataType == hash)
                {
                    throw new ArgumentException("Type already exists.");
                }
            }
            CreateDataTable<T>(DefaultDataTableSize);
        }
        private static void CreateMainTable(int size = 1024)
        {
            //var bytes = sizeof(LookupTable);
            var bytes = (sizeof(int) * 3) + (size * sizeof(IntPtr));
            Table = CAllocation.Malloc(bytes);
            CAllocation.MemSet(Table, 0, bytes);
            *TablePtr = new LookupTable();
            TablePtr->Entries = 0;
            //TablePtr->MaxSize = sizeof(Ptr_8) / sizeof(IntPtr);
            TablePtr->MaxSize = size;
            TablePtr->AllocatedBytes = bytes;
        }
        private static void CreateObjectTable(int size = 1024)
        {
            //var bytes = sizeof(ObjectTable);
            var bytes = ( sizeof(int) * 3 ) + ( size * sizeof(CObject) );
            var entry = CAllocation.Malloc(bytes);
            CAllocation.MemSet(entry, 0, bytes);
            var table = (ObjectTable*)entry;
            *table = new ObjectTable();
            table->Entries = 0;
            //table->MaxSize = sizeof(CObject_Ptr_8) / sizeof(CObject);
            table->MaxSize = size;
            table->AllocatedBytes = bytes;
            *((IntPtr*)&TablePtr->Value0 + TablePtr->Entries++) = entry;
        }
        private static void AddNewObject(ObjectTable* table, CObject cObject)
        {
            if (table->Entries >= table->MaxSize)
            {
                throw new Exception("Not enough space in table");
            }
            *(CObject*)(&table->Value0 + table->Entries++) = cObject;
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
                DataSize = sizeof(T)
            };
            entry.DataPtr = CAllocation.Malloc(entry.DataSize);
            CAllocation.MemSet(entry.DataPtr, 0, entry.DataSize);
            *(T*)entry.DataPtr = data;
            var next = &table->Value0 + table->Entries++;
            *next = entry;
            ( (MetaCObject*)cObject.MetaPtr )->AddRef((IntPtr)next);
        }
        private static void CreateDataTable<T>(int size = 1024) where T : unmanaged
        {
            //var bytes = sizeof(DataTable);
            var bytes = ( sizeof(int) * 3 ) + ( size * sizeof(Data) );
            var entry = CAllocation.Malloc(bytes);
            CAllocation.MemSet(entry, 0, bytes);
            var table = (DataTable*)entry;
            *table = new DataTable();
            table->Entries = 0;
            //table->MaxSize = sizeof(Data_Ptr_8) / sizeof(Data);
            table->MaxSize = size;
            table->AllocatedBytes = bytes;
            table->DataType = typeof(T).GetHashCode();
            *( (IntPtr*)&TablePtr->Value0 + TablePtr->Entries++ ) = entry;
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
                    if(index < 0 || index > this.MaxSize)
                        return IntPtr.Zero;
                    return ptr[index];
                }
            }
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
                    if (index < 0 || index > this.MaxSize)
                        //return CObject.Null;
                        throw new NullReferenceException(nameof(index));
                    return ptr[index];
                }
            }
        }
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

            public void IterateTable<T>(Ref<T> action) where T : unmanaged
            {
                if(typeof(T).GetHashCode() != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }
                ( (DataTable*)table )->IterateTable(action);
            }
            public void IterateTable<T, U>(RefRef<T, U> action) where T : unmanaged where U : unmanaged
            {
                if (typeof(T).GetHashCode() != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }
                ( (DataTable*)table )->IterateTable(action);
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
        public delegate void Ref<T0>(ref T0 data0);
        public delegate void RefRef<T0, T1>(ref T0 data0, ref T1 data1);
        public delegate void In<T0>(in T0 data0);
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
                    if (index < 0 || index > this.MaxSize)
                        //return Data.Null;
                        throw new NullReferenceException(nameof(index));
                    return ptr[index];
                }
            }

            public void IterateTable<T>(Ref<T> action) where T : unmanaged
            {
                fixed(Data* data_ptr = &this.Value0)
                {
                    for (int i = 0; i < this.MaxSize; i++)
                    {
                        //var data = data_ptr[i];
                        //var data = this.GetDataAtIndex(i);
                        var data = data_ptr + i;
                        if(data->IsValid)
                        {
                            data->UseInternalData(action);
                        }
                        else if(i > this.Entries)
                        {
                            // Debug: Dead space detected, does not guarantee the dead space is indicative of the end of the table, could be a blank entry --- needs additional checking
                            break;
                        }
                    }
                }

            }

            public void IterateTable<T, U>(RefRef<T, U> action) where T : unmanaged where U : unmanaged
            {
                var other_table = GetDataTableOf(typeof(U).GetHashCode());
                for (int i = 0; i < this.MaxSize; i++)
                {
                    var data = this.GetDataAtIndex(i);
                    var data1 = other_table->GetDataAtIndex(i);
                    if (data.IsValid && data1.IsValid)
                    {
                        action(ref *data.ExposeData<T>(), ref *data1.ExposeData<U>());
                    }
                }
            }
        }
        internal struct Data
        {
            public static readonly Data Null = new Data();

            public bool IsInitialised;
            public int DataType;
            public int DataSize;
            public IntPtr DataPtr;
            public bool IsValid => IsInitialised && DataPtr != IntPtr.Zero;
            public T GetInternalData<T>() where T : unmanaged
            {
                if (IsValid)
                    return *(T*)DataPtr;
                throw new NullReferenceException();
            }
            public T* ExposeData<T>() where T : unmanaged => (T*)this.DataPtr;
            public void UseInternalData<T>(Ref<T> action) where T : unmanaged => action(ref *(T*)this.DataPtr);
            public void UseInternalData<T>(In<T> action) where T : unmanaged => action(in *(T*)this.DataPtr);
        }
        internal struct MetaCObject
        {
            public int Entries;
            public int MaxSize;
            public IntPtr DataPtr;

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
            public bool HasDataOf(int type)
            {
                fixed (IntPtr* ptr = &DataPtr)
                {
                    for (int i = 0; i < Entries; i++)
                    {
                        if (type == ((Data*)*(ptr + i))->DataType)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

        }
        internal static IntPtr AllocateMetaCObject(int size = 4)
        {
            var bytes = ( sizeof(int) * 2 ) + ( sizeof(IntPtr) * size );
            var ptr = CAllocation.Malloc(bytes);
            CAllocation.MemSet(ptr, 0, bytes);
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
