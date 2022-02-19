#define GAME_TEST
//#undef GAME_TEST
#define BRUTE_FORCE
#undef BRUTE_FORCE
#define BRUTE_FORCE_RENDERING
#undef BRUTE_FORCE_RENDERING
#define PHYSICS_TEST
#undef PHYSICS_TEST

using ECS;
using ECS.Strings;
using ECS.Graphics;
using ECS.Library;
using ECS.Maths;
using ECS.Physics;
using ECS.UI;
using ECS.Delegates;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using SFML.System;
using SFML.Graphics;

namespace Game_Example
{
    using static Subsystem;
    using static UnmanagedCSharp;
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
        private const string ArialFont = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\arial.ttf";
        private const string Bullet = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Bullet.png";
        private const string ExitButton = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\ExitButton.png";
        private const string StartButton = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\StartButton.png";
        private const string Placeholder = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block 2.png";

        public static Texture StartTexture;
        public static Texture ExitTexture;
        public static Texture SpawnTexture;
        public static Texture BulletTexture;
        public static ECS.UI.Font Arial;
        public override EngineSettings Settings => new EngineSettings(64, 1024000, 1024000, 64, new Vector2u(1920, 1080), "Engine Window", false, true, 60);
        public override void Initialise()
        {
            StartTexture = new Texture(StartButton);
            ExitTexture = new Texture(ExitButton);
            SpawnTexture = new Texture(Placeholder);
            BulletTexture = new Texture(Bullet);
            Arial = new ECS.UI.Font(ArialFont);

#if GAME_TEST
            AddNewSubsystem<NewCollisionSubsystem>();
            AddNewSubsystem<PlayerSubsystem>();
            AddNewSubsystem<EnemySubsystem>();
            AddNewDataType<PlayerData>();
            AddNewDataType<EnemyData>();
            AddNewDataType<BulletData>();
#elif BRUTE_FORCE || BRUTE_FORCE_RENDERING
            AddNewSubsystem<MovementSubsystem>();
#endif
            var startButton = CObject.New();
            startButton.AddData(StartTexture);
            startButton.AddData(new Transform(EngineWindow.WindowDimensions.X / 2, EngineWindow.WindowDimensions.Y / 2, 128, 64, 0, 0, 0, Anchor.TOP_LEFT));
            startButton.AddData(new CString("StartButton"));
            startButton.AddData(new Button(() => StartDemo()));

            var button = CObject.New();
            button.AddData(ExitTexture);
            button.AddData(new Transform(-128, 0, 128, 64, 0, 0, 0, Anchor.TOP_RIGHT));
            button.AddData(new Button(() => Engine.Stop()));

        }
        public void StartDemo()
        {
#if GAME_TEST
            CObject.Destroy(Objects.Get("StartButton"));

            var fpsCounter = CObject.New();
            fpsCounter.AddData(new Text("FPS: ", Arial, 20));
            fpsCounter.AddData(new Transform(0, 60, 0, 0, 0, 0));
            fpsCounter.AddData(new CString("FPSCounter"));

            var enemyCount = CObject.New();
            enemyCount.AddData(new Text("Enemies: 0", Arial, 20));
            enemyCount.AddData(new Transform(0, 40, 0, 0, 0, 0));
            enemyCount.AddData(new CString("EnemyCount"));

            var text = CObject.New();
            text.AddData(new Text("Health: NaN", Arial, 20));
            text.AddData(new Transform(0, 20, 0, 0, 0, 0));
            text.AddData(new CString("Health"));

            var text2 = CObject.New();
            text2.AddData(new Text("Kill count: 0", Arial, 20));
            text2.AddData(new Transform(0, 0, 0, 0, 0, 0));
            text2.AddData(new CString("KillCounter"));

            var player = CObject.New();
            player.AddData(SpawnTexture.SetModifiedTextureColor(Color.Blue));
            player.AddData(new Transform(100, 100, 32, 32));
            player.AddData(new PlayerData(100, 100, 1f));
            player.AddData(new Collider());
#if PHYSICS_TEST
            player.AddData(new PhysicsBody());
#endif
            player.AddData(new CString("Player 1"));

#elif BRUTE_FORCE || BRUTE_FORCE_RENDERING
            AddNewSubsystem<MovementSubsystem>();

            var counter = 1000;
            for(int y = 0; y < counter; y++)
            {
                for(int x = 0; x < counter; x++)
                {
                    var cObject = CObject.New();
                    cObject.AddData(new Transform(x * 33, y * 33, 32, 32));
#if BRUTE_FORCE_RENDERING
                    cObject.AddData(SpawnTexture);
#else
                    cObject.AddData(SpawnTexture.CalculateShouldDraw(cObject.GetData<Transform>().BoundingBox, EngineWindow.GetWindowBoundingBox));
#endif
                }
            }
#endif
            StartSubsystems();
        }
    }

#if BRUTE_FORCE || BRUTE_FORCE_RENDERING
    internal class MovementSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            UnmanagedCSharp.Objects.Iterate((ref Transform transform) =>
            {
                transform.Position += new Vector2f(1, 1);
            });
        }
    }
#endif
    
    internal class PlayerSubsystem : Subsystem
    {
        public CObject PlayerObject;
        public ByRefData<Text> Health;
        public ByRefData<Text> FPS;
        public override void Startup()
        {
            base.Startup();
            PlayerObject = Objects.Get("Player 1");
            Health = Objects.GetDataRef<Text>("Health");
            FPS = Objects.GetDataRef<Text>("FPSCounter");
        }
        public override void Update(float deltaSeconds)
        {
#if PHYSICS_TEST
            PlayerObject.WriteData2((ref PhysicsBody body, ref PlayerData player) =>
            {
                if (Input.GetKeyPressed(Key.W))
                {
                    body.ApplyForce(new Vector2f(0, -player.Speed * deltaSeconds));
                }
                if (Input.GetKeyPressed(Key.S))
                {
                    body.ApplyForce(new Vector2f(0, player.Speed * deltaSeconds));
                }
                if (Input.GetKeyPressed(Key.A))
                {
                    body.ApplyForce(new Vector2f(-player.Speed * deltaSeconds, 0));
                }
                if (Input.GetKeyPressed(Key.D))
                {
                    body.ApplyForce(new Vector2f(player.Speed * deltaSeconds, 0));
                }

            });
#else
            PlayerObject.WriteData2((ref Transform transform, ref PlayerData player) =>
            {
                if (Input.GetKeyPressed(Key.W))
                {
                    transform.Position += new Vector2f(0, -player.Speed * deltaSeconds);
                }
                if (Input.GetKeyPressed(Key.S))
                {
                    transform.Position += new Vector2f(0, player.Speed * deltaSeconds);
                }
                if (Input.GetKeyPressed(Key.A))
                {
                    transform.Position += new Vector2f(-player.Speed * deltaSeconds, 0);
                }
                if (Input.GetKeyPressed(Key.D))
                {
                    transform.Position += new Vector2f(player.Speed * deltaSeconds, 0);
                }
                player.AttackSpeed -= deltaSeconds;
                if (player.AttackSpeed <= 0 && Input.GetMouseButtonPressed(0))
                {
                    var direction = transform.Position.Direction(Input.GetMousePosition());
                    var cBullet = CObject.New();
                    cBullet.AddData(GameEngine.BulletTexture);
                    cBullet.AddData(new Transform(transform.Position.X, transform.Position.Y, 12, 17, 6, 8.5f, direction.ToRotation()));
                    cBullet.AddData(new BulletData(direction, 200f, 1));
                    cBullet.AddData(new Collider());
                    player.AttackSpeed = 0.1f;
                }
            });

            Health.VolatileWrite((ref Text text) =>
            {
                text.ChangeText("Health: " + PlayerObject.GetData<PlayerData>().Health);
            });


            Objects.IterateWithObject((ref BulletData bullet, ref Transform transform, ref CObject cObject) =>
            {
                transform.Position += bullet.Direction * bullet.Speed * deltaSeconds;
                bullet.LifeTime -= deltaSeconds;
                if (bullet.LifeTime <= 0)
                {
                    CObject.Destroy(cObject);
                }
            });
#endif
            FPS.VolatileWrite((ref Text text) =>
            {
                text.ChangeText("FPS: " + Math.Floor(CTime.FPS));
            });
        }
    }
    internal class EnemySubsystem : Subsystem
    {
        public static int EnemyLimit = 250;
        public static int EnemyCount = 0;
        private int spawnAmount = 1;
        private float spawnTimer = 1f;
        public override void Update(float deltaSeconds)
        {
            spawnTimer -= deltaSeconds;
            if(spawnTimer <= 0 && EnemyCount < EnemyLimit)
            {
                SpawnEnemy(spawnAmount);
                EnemyCount += spawnAmount;
                spawnTimer = 1f;
                Objects.GetDataRef<Text>("EnemyCount").VolatileWrite((ref Text text) =>
                {
                    text.ChangeText("Enemies: " + EnemyCount);
                });
            }
#if !PHYSICS_TEST
            var PlayerTarget = Objects.GetData<Transform>("Player 1");

            Objects.Iterate((ref EnemyData enemy, ref Transform transform) =>
            {
                var direction = (PlayerTarget.Position - transform.Position).Normalise();

                transform.Position += direction * enemy.Speed * deltaSeconds;
            });
#endif
        }

        public static void SpawnEnemy(int count = 1)
        {
            for(int i = 0; i < count; i++)
            {
                var enemy = CObject.New();
                enemy.AddData(GameEngine.SpawnTexture.SetModifiedTextureColor(Color.Red));
                enemy.AddData(new Transform(RNG.Next(0, (int)Engine.EngineWindow.WindowDimensions.X), RNG.Next(0, (int)Engine.EngineWindow.WindowDimensions.Y), 32, 32));
                enemy.AddData(new EnemyData(10, 0, 100));
#if PHYSICS_TEST
                enemy.AddData(new PhysicsBody());
#endif
                enemy.AddData(new Collider());
                enemy.AddData(new CString("Enemy"));
            }
        }
    }
    internal class NewCollisionSubsystem : CollisionSubsystem
    {
        public CObject PlayerObject;
        public ByRefData<Text> KillCounter;

        public override void Startup()
        {
            base.Startup();
            VerifyCollisions = true;
            PlayerObject = Objects.Get("Player 1");
            KillCounter = Objects.GetDataRef<Text>("KillCounter");
#if !PHYSICS_TEST
            Objects.IgnoreCollisions<EnemyData>();
#endif
        }
        public override void Update(float deltaSeconds)
        {
            base.Update(deltaSeconds);
            if(GetPossibleCollisions.ContainsKey(PlayerObject))
            {
                var collisionsCount = GetPossibleCollisions[PlayerObject].Where(x => x.HasDataOf<EnemyData>());
                
                PlayerObject.WriteData((ref PlayerData data) =>
                {
                    foreach (var item in collisionsCount)
                    {
                        var enemyData = item.GetData<EnemyData>();
                        if(enemyData.AttackCooldown <= 0)
                        {
                            data.Health -= enemyData.Damage;
                            item.WriteData((ref EnemyData data) => data.AttackCooldown = 1f);
                        }
                        else
                        {
                            item.WriteData((ref EnemyData data) => data.AttackCooldown -= deltaSeconds);
                        }

                        if(data.Health <= 0)
                        {
                            CObject.Destroy(ref PlayerObject);
                            return;
                        }
                    }
                });
            }

            var bullets = GetPossibleCollisions.Where(x => x.Key.HasDataOf<BulletData>());
            foreach (var bullet in bullets)
            {
                var hit = bullet.Value.Where(x => x.HasDataOf<EnemyData>());
                var count = hit.Count();
                if (count == 0)
                    continue;
                if(hit.First() is CObject cObject && !cObject.IsActiveNull())
                {
                    cObject.WriteData((ref EnemyData data) =>
                    {
                        data.Health -= bullet.Key.GetData<BulletData>().Damage;
                        CObject.Destroy(bullet.Key);
                        if (data.Health <= 0)
                        {
                            CObject.Destroy(hit.First());
                            EnemySubsystem.EnemyCount--;
                            var kills = "Kill count: ";
                            PlayerObject.WriteData((ref PlayerData player) =>
                            {
                                ++player.KillCount;
                                kills += player.KillCount;
                            });
                            KillCounter.VolatileWrite((ref Text text) => text.ChangeText(kills));
                        }
                    });
                }
            }
            
        }
    }
    internal struct PlayerData : IComponentData
    {
        public int KillCount;
        public int Health;
        public float Speed;
        public float AttackSpeed;
        public PlayerData(int health, float speed, float attackSpeed)
        {
            Health = health;
            Speed = speed;
            KillCount = 0;
            AttackSpeed = attackSpeed;
        }
    }
    internal struct EnemyData : IComponentData
    {
        public int Health;
        public int Damage;
        public float Speed;
        public float AttackSpeed;
        public float AttackCooldown;
        public EnemyData(int health, int dmg, float speed = 10, float attackSpeed = 1f)
        {
            Health = health;
            Damage = dmg;
            Speed = speed;
            AttackSpeed = attackSpeed;
            AttackCooldown = 1f;
        }
    }
    public struct BulletData : IComponentData
    {
        public int Damage;
        public Vector2f Direction;
        public float Speed;
        public float LifeTime;
        public BulletData(Vector2f dir, float speed, int dmg = 10)
        {
            Damage = dmg;
            Direction = dir;
            Speed = speed;
            LifeTime = 3f;
        }
    }
}
