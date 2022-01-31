#pragma warning disable IDE0022, IDE0009

using ECS;
using ECS.Graphics;
using ECS.Window;
using ECS.Maths;
using ECS.Physics;

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SFML.System;
using SFML.Graphics;
using SFML.Window;

using System.Diagnostics.CodeAnalysis;

namespace ECS.Library
{
    using static UnmanagedCSharp;
    using static Keyboard;
    public static class Time
    {
        public static float DeltaTime { get; internal set; } = 0f;
    }
    public abstract class Engine
    {

        internal static Engine MainEngine = null;
        internal static EngineWindow MainWindow => MainEngine?.ThisWindow;


        internal EngineWindow ThisWindow = null;
        public abstract void Initialise();
        public virtual EngineSettings Settings => new EngineSettings(4, 1024, 1024, 10, new SFML.Window.VideoMode(800, 600), "Window");
        private void GameLoop() // might make "public virtual"
        {
            foreach (var subsystem in Collection.Subsystems)
            {
                var time = DateTime.Now;
                subsystem.Update(Time.DeltaTime);
                Console.WriteLine(subsystem.Name + ": " + ( DateTime.Now - time ).TotalMilliseconds);
            }
        }
        public static void Start(Type engineType)
        {
            if (engineType.IsSubclassOf(typeof(Engine)))
            {
                MainEngine = (Engine)Activator.CreateInstance(engineType);
                MainEngine.ThisWindow = new EngineWindow(MainEngine.Settings.VideoMode, MainEngine.Settings.WindowName);
                Entry(MainEngine.Settings.MainTableSize, MainEngine.Settings.ObjectTableSize, MainEngine.Settings.DataTableSize, MainEngine.Settings.TextureTableSize);
                Collection.AddNewSubsystem(typeof(RenderSubsystem));
                //Collection.AddNewSubsystem(typeof(MovementSubsystem));
                Collection.AddNewSubsystem(typeof(ColorRandomiserSubsystem));
                //Collection.AddNewSubsystem(typeof(ColorGradientSubsystem));
                //Collection.AddNewSubsystem(typeof(PlayerSubsystem));
                //Collection.AddNewSubsystem(typeof(ControlInputSubsystem));
                Collection.AddNewSubsystem(typeof(PhysicsSubsystem));
                AddNewDataType<Texture>();
                AddNewDataType<Transform>();
                AddNewDataType<PhysicsBody>();
                //AddNewDataType<PlayerData>();
                /*foreach (var subsystem in Collection.Subsystems)
                {
                    subsystem.Startup();
                }*/
                MainEngine.Initialise();
                while (MainWindow.IsOpen)
                {
                    MainWindow.DispatchEvents();
                    var time = DateTime.Now;
                    MainEngine.GameLoop();
                    MainWindow.Display();
                    MainWindow.Clear();
                    Time.DeltaTime = (float)( DateTime.Now - time ).TotalSeconds;
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
        public VideoMode VideoMode;
        public string WindowName;
        public EngineSettings(int mSize, int otSize, int dtSize, int texSize, VideoMode mode, string name)
        {
            MainTableSize = mSize;
            ObjectTableSize = otSize;
            DataTableSize = dtSize;
            TextureTableSize = texSize;
            VideoMode = mode;
            WindowName = name;
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
        internal Subsystem()
        {
            Name = GetType().Name;
        }
        public readonly string Name;
        public abstract void Update(float deltaSeconds);
    }
    public class MovementSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Transform transform) =>
            {
                transform.Position += new Vector2f(0.1f, 0.1f);
            });
        }
    }
    public class RenderSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Texture texture, ref Transform transform) =>
            {
                var states = new RenderStates(transform.SFMLTransform);
                texture.Draw(Engine.MainWindow, states);
            });
        }
    }
    

    public class ColorRandomiserSubsystem : Subsystem
    {
        private float Timer = 0f;
        public override void Update(float deltaSeconds)
        {
            Timer += deltaSeconds;
            if (Timer > 1f)
            {
                Entities.Iterate((ref Texture texture) =>
                {
                    texture.RandomiseColor();
                });
                Timer = 0f;
            }
        }
    }

    public class PhysicsSubsystem : Subsystem
    {
        private Vector2u Window = new Vector2u(Engine.MainEngine.Settings.VideoMode.Width, Engine.MainEngine.Settings.VideoMode.Height);
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref PhysicsBody body, ref Transform transform, ref Texture texture) =>
            {
                var size = texture.GetSize;

                body.Velocity += Constants.Gravity * deltaSeconds;

                if(transform.Position.Y + size.Y > Window.Y || transform.Position.Y < 0)
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

    /*
    public struct PlayerData : IComponentData
    {
        public int Health;
    }
    public class ControlInputSubsystem : Subsystem
    {
        public Key Current => Engine.MainWindow.CurrentKey;
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref PlayerData playerData, ref Transform transform) =>
            {
                if (Current == Key.W)
                {
                    transform.Position += new Vector2f(0, -1f);
                }
                if (Current == Key.S)
                {
                    transform.Position += new Vector2f(0, 1f);
                }
                if (Current == Key.A)
                {
                    transform.Position += new Vector2f(-1f, 0f);
                }
                if (Current == Key.D)
                {
                    transform.Position += new Vector2f(1f, 0f);
                }
            });
        }
    }


    /*
    public abstract class Subsystem<Data0> : Subsystem where Data0 : IComponentData
    {
        //protected RefDataTable RefTable0;
        //protected RefObjectTable RefTable0;
        public override void Startup()
        {
            //RefTable0 = RefDataTable.CreateFromUnknownType<Data0>();
            //RefTable0 = RefObjectTable.New();
        }
    }
    public class MovementSubsystem : Subsystem<Transform>
    {
        public override void Update(float deltaSeconds)
        {
            RefObjects.IterateTable((ref Transform transform) =>
            {
                transform.Position += new Vector2f(0.1f, 0.1f);
            });
        }
    }
    public abstract class Subsystem<Data0, Data1> : Subsystem where Data0 : IComponentData where Data1 : IComponentData
    {
        //protected RefDataTable RefTable0;
        //protected RefDataTable RefTable1;
        public override void Startup()
        {
            //RefTable0 = RefDataTable.CreateFromUnknownType<Data0>();
            //RefTable1 = RefDataTable.CreateFromUnknownType<Data1>();
        }
    }
    public class RenderSubsystem : Subsystem<Texture, Transform>
    {
        public override void Update(float deltaSeconds)
        {
            RefObjects.IterateTable((ref Texture texture, ref Transform transform) =>
            {
                var states = new RenderStates(transform.SFMLTransform);
                texture.Draw(Engine.MainWindow, states);
            });
        }
    }
    
    public struct PlayerData : IComponentData
    {
        public int Health;
    }
    public class PlayerSubsystem : Subsystem<PlayerData, Transform>
    {
        public override void Update(float deltaSeconds)
        {
            RefObjects.IterateTable((ref PlayerData player, ref Transform transform) =>
            {
                player.Health += 1;
                if(Engine.MainWindow.CurrentKey == Keyboard.Key.W)
                {
                    transform.Position += new Vector2f(0, -10f);
                }
                if (Engine.MainWindow.CurrentKey == Keyboard.Key.S)
                {
                    transform.Position += new Vector2f(0, 10f);
                }
                if (Engine.MainWindow.CurrentKey == Keyboard.Key.A)
                {
                    transform.Position += new Vector2f(-10f, 0f);
                }
                if (Engine.MainWindow.CurrentKey == Keyboard.Key.D)
                {
                    transform.Position += new Vector2f(10f, 0f);
                }
                Console.WriteLine(player.Health);
            });
        }
    }
    
    public class ColorRandomiserSubsystem : Subsystem<Texture>
    {
        private float Timer = 0f;
        public override void Update(float deltaSeconds)
        {
            Timer += deltaSeconds;
            if(Timer > 1f)
            {
                RefObjects.IterateTable((ref Texture texture) =>
                {
                    texture.RandomiseColor();
                });
                Timer = 0f;
            }
        }
    }
    */
}
