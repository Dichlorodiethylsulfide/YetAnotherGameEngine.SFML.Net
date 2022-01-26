using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SFML.Window;
using SFML.Graphics;
using SFML.System;
using System.Diagnostics.CodeAnalysis;

namespace ECSLibrary
{
    public static class Time
    {
        public static float DeltaTime { get; internal set; } = 0f;
    }
    public abstract class Engine
    {
        internal class EngineWindow : RenderWindow
        {
            public EngineWindow(VideoMode mode, string name) : base(mode, name)
            {
                SetFramerateLimit(60);
            }
        }

        internal static Engine MainEngine = null;
        internal static EngineWindow MainWindow => MainEngine?.ThisWindow;


        internal EngineWindow ThisWindow = null;
        public abstract void Initialise();
        private void GameLoop() // might make "public virtual"
        {
            var time = DateTime.Now;
            foreach(var subsystem in Collection.Subsystems)
            {
                subsystem.Update(Time.DeltaTime);
            }
            Time.DeltaTime = (float)(DateTime.Now - time).TotalMilliseconds;
        }
        public static void Start(Type engineType)
        {
            if(engineType.IsSubclassOf(typeof(Engine)))
            {
                MainEngine = (Engine)Activator.CreateInstance(engineType);
                MainEngine.ThisWindow = new EngineWindow(new VideoMode(800, 600), "Engine Window");
                //var task = new Task(() => StartRenderLoop());
                //task.Start();
                //Thread.Sleep(1000);
                Collection.AddNewSubsystem(typeof(RenderSubsystem));
                Collection.AddNewSubsystem(typeof(MovementSubsystem));
                Collection.AddNewType(typeof(RenderData));
                foreach (var subsystem in Collection.Subsystems)
                {
                    subsystem.Startup();
                }
                MainEngine.Initialise();
                while (MainWindow.IsOpen)
                {
                    MainWindow.DispatchEvents();
                    MainEngine.GameLoop();
                    MainWindow.Display();
                    MainWindow.Clear();
                }
                //task.Wait();
            }
        }
        /*
        private static void StartRenderLoop(RenderSubsystem render)
        {
            MainEngine.ThisWindow = new EngineWindow(new VideoMode(800, 600), "Engine Window");
            while (MainWindow.IsOpen)
            {
                MainWindow.DispatchEvents();
                render.Update(0f);
                MainWindow.Display();
                MainWindow.Clear();
            }
        }
        */
    }
    public struct Object : IEquatable<Object>
    {
        private static int incrementer = 1;
        public static readonly Object Null = new Object();
        public int ID { get; private set; }
        public bool IsNull() => this.ID == 0;
        public override int GetHashCode() => this.ID;
        public bool Equals([AllowNull] Object other) => this.ID == other.ID;
        public static Object New(params IComponentData[] datas)
        {
            var obj = new Object
            {
                ID = incrementer++
            };
            Collection.AddNewObject(obj, datas);
            return obj;
        }
    }
    public interface IComponentData { }
    public struct RenderData : IComponentData
    {
        public readonly Sprite Sprite;
        public RenderData(Sprite sprite) => this.Sprite = sprite;
    }
    public static class Collection
    {
        internal static readonly List<Object> Objects = new List<Object>();
        internal static readonly List<Subsystem> Subsystems = new List<Subsystem>();
        internal static readonly Dictionary<Type, Dictionary<Object, IComponentData>> ComponentsByType = new Dictionary<Type, Dictionary<Object, IComponentData>>();
        public static void AddNewObject(Object obj, params IComponentData[] datas)
        {
            if(!Objects.Contains(obj))
            {
                Objects.Add(obj);
                foreach(var data in datas)
                {
                    var type = data.GetType();
                    if(ComponentsByType.ContainsKey(type))
                    {
                        ComponentsByType[type].Add(obj, data);
                    }
                }
            }
        }
        public static void RemoveObject(Object obj)
        {
            Objects.Remove(obj);
            foreach(var item in ComponentsByType.Values)
            {
                if(item.ContainsKey(obj))
                {
                    item.Remove(obj);
                }
            }
        }
        internal static void AddNewType(Type type)
        {
            if(typeof(IComponentData).IsAssignableFrom(type))
            {
                ComponentsByType.Add(type, new Dictionary<Object, IComponentData>());
            }
        }
        internal static void AddNewSubsystem(Type type)
        {
            if(type.IsSubclassOf(typeof(Subsystem)))
            {
                Subsystems.Add((Subsystem)Activator.CreateInstance(type));
            }
        }
    }
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
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _dictionary.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dictionary.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public abstract class Subsystem
    {
        public abstract void Startup();
        public abstract void Update(float deltaSeconds);
    }
    public abstract class Subsystem<Data> : Subsystem where Data : IComponentData
    {
        protected ReadOnlyDictionary<Object, IComponentData> ComponentData { get; private set; } = null;
        public override void Startup()
        {
            if (Collection.ComponentsByType.TryGetValue(typeof(Data), out var values))
            {
                ComponentData = new ReadOnlyDictionary<Object, IComponentData>(values);
            }
        }
    }
    public class RenderSubsystem : Subsystem<RenderData>
    {
        public override void Update(float deltaSeconds)
        {
            if (ComponentData != null)
            {
                foreach (var item in ComponentData.Values)
                {
                    ((RenderData)item).Sprite.Draw(Engine.MainWindow, RenderStates.Default);
                }
            }
        }
    }
    public class MovementSubsystem : Subsystem<RenderData>
    {
        public override void Update(float deltaSeconds)
        {
            if (ComponentData != null)
            {
                foreach (var item in ComponentData.Values)
                {
                    ((RenderData)item).Sprite.Position += new Vector2f(0.1f, 0.1f);
                }
            }
        }
    }
}
