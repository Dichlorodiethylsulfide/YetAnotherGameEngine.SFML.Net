using ECS;
using ECS.Graphics;
using ECS.Library;
using ECS.Maths;
using ECS.Physics;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using SFML.System;
using SFML.Graphics;

namespace Game_Example
{
    using static Subsystem;
    using static SFML.Window.Keyboard;
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            Engine.Start(typeof(GameEngine));
            Console.WriteLine("Goodbye World!");
        }
    }
    internal class GameEngine : Engine
    {
        private const string Placeholder = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block 2.png";
        public override EngineSettings Settings => new EngineSettings(64, 1024000, 1024000, 64, new Vector2u(1280, 720), "Engine Window", false, true, true);
        public override void Initialise()
        {
            AddNewSubsystem<NewCollisionSubsystem>();
            AddNewSubsystem<PlayerSubsystem>();
            AddNewSubsystem<EnemySubsystem>();
            AddNewDataType<PlayerData>();
            AddNewDataType<EnemyData>();
            var texture = new Texture(Placeholder);

            var player = CObject.New();
            player.AddData(texture.SetModifiedTextureColor(Color.Blue));
            player.AddData(new Transform(100, 100, 32, 32));
            player.AddData(new PlayerData(100, 20));
            player.AddData(new Collider());

            for(int i = 0; i < 10; i++)
            {
                var enemy = CObject.New();
                enemy.AddData(texture.SetModifiedTextureColor(Color.Red));
                enemy.AddData(new Transform(Random.Next(0, (int)Settings.WindowDimensions.X), Random.Next(0, (int)Settings.WindowDimensions.Y), 32, 32));
                enemy.AddData(new EnemyData(10));
                enemy.AddData(new Collider());
            }
        }
    }
    internal class PlayerSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref PlayerData player, ref Transform transform) =>
            {
                if (Input.GetKeyPressed(Key.W))
                {
                    transform.Position += new Vector2f(0, -100f * player.Speed) * deltaSeconds;
                }
                if (Input.GetKeyPressed(Key.S))
                {
                    transform.Position += new Vector2f(0, 100f * player.Speed) * deltaSeconds;
                }
                if (Input.GetKeyPressed(Key.A))
                {
                    transform.Position += new Vector2f(-100f * player.Speed, 0) * deltaSeconds;
                }
                if (Input.GetKeyPressed(Key.D))
                {
                    transform.Position += new Vector2f(100f * player.Speed, 0) * deltaSeconds;
                }
            });
        }
    }
    internal class EnemySubsystem : Subsystem
    {
        public Transform PlayerTarget;
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref PlayerData player, ref Transform transform) => { PlayerTarget = transform; });

            Entities.Iterate((ref EnemyData enemy, ref Transform transform) =>
            {
                var direction = (PlayerTarget.Position - transform.Position).Normalise();

                transform.Position += direction * enemy.Speed * deltaSeconds;
            });
        }
    }
    internal class NewCollisionSubsystem : CollisionSubsystem
    {
        public override void Update(float deltaSeconds)
        {
            var collisionsCount = GetCollisionsThisFrame.Where(x => x.This.HasDataOf<PlayerData>() != -1 || x.Other.HasDataOf<PlayerData>() != -1).Count();
            Entities.IterateWithObject((ref PlayerData player, ref CObject cObject) => { player.Health -= collisionsCount; if (player.Health <= 0) { CObject.Destroy(cObject); } });
        }
    }
    internal struct PlayerData : IComponentData
    {
        public int Health;
        public float Speed;
        public PlayerData(int health, float speed)
        {
            Health = health;
            Speed = speed;
        }
    }
    internal struct EnemyData : IComponentData
    {
        public int Damage;
        public float Speed;
        public EnemyData(int dmg, float speed = 10)
        {
            Damage = dmg;
            Speed = speed;
        }
    }
}
