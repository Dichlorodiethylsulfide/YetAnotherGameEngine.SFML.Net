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

namespace ECS 
{
    using static CAllocation;
    using static Debug;
    using static UnmanagedCSharp;
    /*
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
    */
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
    }*/
    /*
    public struct ComponentData : IDisposable, IComponentData
    {
        public bool IsInitialised { get; private set; }
        internal int DataType;
        internal IntPtr DataPointer;
        public T ReadData<T>() where T : unmanaged
        {
            var hash = typeof(T).GetHashCode();
            if (DataType != 0 && DataType == hash)
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
            if (DataPointer != IntPtr.Zero)
            {
                CAllocation.Free(DataPointer);
            }
        }
        public static ComponentData New<T>() where T : unmanaged
        {
            var data = new ComponentData();
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
    }*/
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
        internal unsafe int HasDataOf<T>() where T : unmanaged => this.HasDataOf(typeof(T).GetHashCode());
        internal unsafe int HasDataOf(int type)
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
        private const int DataTableStartIndex = 2; // Object Table at 0, Texture table will be 1, so this should be 2 when done
        private static int DefaultDataTableSize = 0;
        private static IntPtr Table = IntPtr.Zero;
        internal static LookupTable* TablePtr => (LookupTable*)Table;
        internal static ObjectTable* ObjectTablePtr => (ObjectTable*)*&TablePtr->Value0;
        internal static TextureTable* TextureTablePtr => (TextureTable*)*((&TablePtr->Value0) + 1);

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
            CreateMainTable(mainSize + DataTableStartIndex);
            CreateObjectTable(objectSize);
            CreateTextureTable(textureSize);
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
                            // Add counter
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
        }
        public struct RefObjectTable
        {
            private IntPtr table;


            public void Iterate<T>(Ref<T> action, bool multi_threaded = false) where T : unmanaged
            {
                /*var type = typeof(T).GetHashCode();
                if (typeof(T).GetHashCode() != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }*/
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            public void Iterate<T, U>(RefRef<T, U> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged
            {
                /*var type = typeof(T).GetHashCode();
                if (typeof(T).GetHashCode() != type)
                {
                    throw new ArgumentException("Data type mismatch.");
                }*/
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
            }
            public void Iterate<T, U, V>(RefRefRef<T, U, V> action, bool multi_threaded = false) where T : unmanaged where U : unmanaged where V : unmanaged
            {
                ( (ObjectTable*)table )->IterateTable(action, multi_threaded);
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
        public delegate void RefWithIteration<T0>(ref T0 data0, int current_iteration, int max_iteration);
        public delegate void RefRef<T0, T1>(ref T0 data0, ref T1 data1);
        public delegate void RefRefRef<T0, T1, T2>(ref T0 data0, ref T1 data1, ref T2 data2);
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
            public void UseInternalData<T>(RefWithIteration<T> action, int index, int max) where T : unmanaged => action(ref *(T*)this.DataPtr, index, max);
            public void UseInternalData<T>(In<T> action) where T : unmanaged => action(in *(T*)this.DataPtr);

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
                        Data* data = (Data*)ptr;
                        if (data->IsValid)
                        {
                            Data.Destroy(*data);
                        }
                        //*ptr = IntPtr.Zero;
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
        public Transform(float x, float y) : this(new Vector2f(x, y)) { }
        public Transform(Vector2f position)
        {
            myOrigin = new Vector2f();//new Vector2f(0.5f, 0.5f);
            myPosition = position;
            myRotation = 0;
            myScale = new Vector2f(1, 1);
            myTransformNeedUpdate = true;
            myInverseNeedUpdate = true;
            myTransform = default;
            myInverseTransform = default;
        }

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
        //internal FloatRect InternalLocalBounds;
        internal Vector2u Size;
        internal IntPtr TexturePtr;
        internal TextureEntry* Entry => (TextureEntry*)TexturePtr;
        public Texture(string filename)
        {
            TexturePtr = TryAddTexture(filename);
            Vertices = new Vertex4();
            Size = new Vector2u();
            //InternalLocalBounds = new FloatRect();
            UpdateTexture(TexturePtr);
        }
        public void UpdateTexture(IntPtr texture)
        {
            if (texture != IntPtr.Zero)
            {
                this.TexturePtr = texture;

                Size = Entry->GetSize();
                Entry->TextureRect = new IntRect(0, 0, Convert.ToInt32(Size.X), Convert.ToInt32(Size.Y));

                //var bounds = this.InternalLocalBounds;
                var left = Convert.ToSingle(Entry->TextureRect.Left);
                var right = left + Entry->TextureRect.Width;
                var top = Convert.ToSingle(Entry->TextureRect.Top);
                var bottom = top + Entry->TextureRect.Height;

                Vertices.Vertex0 = new Vertex(new Vector2f(0, 0), Color.White, new Vector2f(left, top));
                Vertices.Vertex1 = new Vertex(new Vector2f(0, Size.Y), Color.White, new Vector2f(left, bottom));
                Vertices.Vertex2 = new Vertex(new Vector2f(Size.X, 0), Color.White, new Vector2f(right, top));
                Vertices.Vertex3 = new Vertex(new Vector2f(Size.X, Size.Y), Color.White, new Vector2f(right, bottom));
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
        public Color GetColor()
        {
            return Vertices.Vertex0.Color;
        }
        public Vector2u GetSize => Size;
        public void Draw(RenderTarget target, RenderStates states)
        {
            states.CTexture = Entry->TexturePtr;
            ( (RenderWindow)target ).Draw(Vertices, PrimitiveType.TriangleStrip, states);;
        }
    }
}
namespace ECS.Window
{
    public class EngineWindow : RenderWindow
    {
        public Keyboard.Key CurrentKey { get; internal set; } = Keyboard.Key.Unknown;
        public EngineWindow(VideoMode mode, string name) : base(mode, name)
        {
            SetFramerateLimit(60);
            this.Closed += OnQuit;
            this.KeyPressed += OnKeyPress;
            this.KeyReleased += OnKeyRelease;
        }

        private void OnQuit(object sender, EventArgs args)
        {
            this.Close();
        }

        private void OnKeyPress(object sender, KeyEventArgs args)
        {
            CurrentKey = args.Code;
        }
        private void OnKeyRelease(object sender, KeyEventArgs args)
        {
            CurrentKey = Keyboard.Key.Unknown; // change
        }
    }
}
namespace ECS.Maths
{
    public static class Constants
    {
        public static readonly Vector2f Gravity = new Vector2f(0, 9.80665f);
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

    /*
    public struct PhysicsCollider
    {
        
    }
    */
}