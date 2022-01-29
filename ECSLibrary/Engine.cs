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

using ECS;

namespace ECS.Library
{
    public static class Time
    {
        public static float DeltaTime { get; internal set; } = 0f;
    }
    public abstract class Engine
    {
        internal class EngineWindow : RenderWindow
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
                CurrentKey = Keyboard.Key.Unknown;
            }
        }

        internal static Engine MainEngine = null;
        internal static EngineWindow MainWindow => MainEngine?.ThisWindow;


        internal EngineWindow ThisWindow = null;
        public abstract void Initialise();
        private void GameLoop() // might make "public virtual"
        {
            var time = DateTime.Now;
            foreach (var subsystem in Collection.Subsystems)
            {
                subsystem.Update(Time.DeltaTime);
            }
            Time.DeltaTime = (float)( DateTime.Now - time ).TotalMilliseconds;
        }
        public static void Start(Type engineType)
        {
            if (engineType.IsSubclassOf(typeof(Engine)))
            {
                MainEngine = (Engine)Activator.CreateInstance(engineType);
                MainEngine.ThisWindow = new EngineWindow(new VideoMode(800, 600), "Engine Window");
                UnmanagedCSharp.Entry(2, 1024, 1024);
                Collection.AddNewSubsystem(typeof(RenderSubsystem));
                Collection.AddNewSubsystem(typeof(MovementSubsystem));
                UnmanagedCSharp.AddNewDataType<Texture>();
                UnmanagedCSharp.AddNewDataType<Transform>();
                /*Collection.AddNewSubsystem(typeof(PlayerSubsystem));
                Collection.AddNewDataType(typeof(Texture));
                Collection.AddNewDataType(typeof(Transform));
                Collection.AddNewDataType(typeof(PlayerData));*/
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
            }
        }
    }
    public static class Collection
    {
        internal static readonly List<Subsystem> Subsystems = new List<Subsystem>();
        internal static void AddNewSubsystem(Type type)
        {
            if (type.IsSubclassOf(typeof(Subsystem)))
            {
                Subsystems.Add((Subsystem)Activator.CreateInstance(type));
            }
        }
    }
    public abstract class Subsystem
    {
        public abstract void Startup();
        public abstract void Update(float deltaSeconds);
    }
    public abstract class Subsystem<Data0> : Subsystem where Data0 : IComponentData
    {
        protected UnmanagedCSharp.RefDataTable RefTable0;
        public override void Startup()
        {
            RefTable0 = UnmanagedCSharp.RefDataTable.CreateFromUnknownType<Data0>();
        }
    }
    public class MovementSubsystem : Subsystem<Transform>
    {
        public override void Update(float deltaSeconds)
        {
            var time = DateTime.Now;
            RefTable0.IterateTable((ref Transform transform) =>
            {
                transform.Position += new Vector2f(0.1f, 0.1f);
            });
            Console.WriteLine("Movement: " + ( DateTime.Now - time ).TotalMilliseconds);
        }
    }
    public abstract class Subsystem<Data0, Data1> : Subsystem where Data0 : IComponentData where Data1 : IComponentData
    {
        protected UnmanagedCSharp.RefDataTable RefTable0;
        protected UnmanagedCSharp.RefDataTable RefTable1;
        public override void Startup()
        {
            RefTable0 = UnmanagedCSharp.RefDataTable.CreateFromUnknownType<Data0>();
            RefTable1 = UnmanagedCSharp.RefDataTable.CreateFromUnknownType<Data1>();
        }
    }
    public class RenderSubsystem : Subsystem<Texture, Transform>
    {
        public override void Update(float deltaSeconds)
        {
            var time = DateTime.Now;
            RefTable0.IterateTable((ref Texture texture, ref Transform transform) =>
            {
                var states = new RenderStates(transform.SFMLTransform);
                texture.Draw(Engine.MainWindow, states);
            });
            Console.WriteLine("Render: " + ( DateTime.Now - time ).TotalMilliseconds);
        }
    }
}
