#define GAME_TEST
//#undef GAME_TEST
#define BRUTE_FORCE
#undef BRUTE_FORCE
#define BRUTE_FORCE_RENDERING
#undef BRUTE_FORCE_RENDERING
#define PHYSICS_TEST
#undef PHYSICS_TEST
#define SHOW_LOGGER
#undef SHOW_LOGGER

using ECS;
using ECS.Animations;
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
#if SHOW_LOGGER
            ECS.Logger.Logger.ReadAvailableLogs();
#else
            Engine.Start(typeof(GameEngine));
#endif
            Console.WriteLine("Goodbye World!");
        }
    }
    internal class GameEngine : Engine
    {
        public static readonly FilePath ArialFont = new FilePath(@"..\..\..\..\Resources\Placeholder\arial.ttf");
        public static readonly FilePath Bullet = new FilePath(@"..\..\..\..\Resources\Placeholder\Bullet.png");
        public static readonly FilePath ExitButton = new FilePath(@"..\..\..\..\Resources\Placeholder\ExitButton.png");
        public static readonly FilePath StartButton = new FilePath(@"..\..\..\..\Resources\Placeholder\StartButton.png");
        public static readonly FilePath SpriteSheet = new FilePath(@"..\..\..\..\Resources\Placeholder\Block_SpriteSheet.png");

        public static Texture SpriteSheetTexture;
        public static Texture StartTexture;
        public static Texture ExitTexture;
        public static Texture SpawnTexture;
        public static Texture BulletTexture;
        public static Animation SquareAnimation;
        public static ECS.UI.Font Arial;

#if !GAME_TEST
        public override EngineSettings Settings => new EngineSettings(64, 1024000, 1024000, 64, new Vector2u(1920, 1080), "Engine Window", false, false, false, 60);
#else
        public override EngineSettings Settings => new EngineSettings(64, 1024000, 1024000, 64, new Vector2u(1920, 1080), "Engine Window", false, true, true, 60);
#endif
        public override void Initialise()
        {
            SpriteSheetTexture = Texture.CreateSpriteSheet(SpriteSheet, new Vector2u(32, 32));
            StartTexture = new Texture(StartButton);
            ExitTexture = new Texture(ExitButton);
            SpawnTexture = SpriteSheetTexture.GetSpriteFromSheet(0);
            BulletTexture = new Texture(Bullet);
            Arial = new ECS.UI.Font(ArialFont);
            //SquareAnimation = new Animation(5);
            //for (int i = 0; i < 5; i++)
            //    SquareAnimation.SetAnimationFrameTo(i, SpriteSheetTexture);
            SquareAnimation = new Animation("SquareAnimation", 5);
            for (int i = 0; i < 5; i++)
                SquareAnimation.SetFrameDataTo(i, ref SpriteSheetTexture);
#if GAME_TEST
            AddNewSubsystem<NewCollisionSubsystem>();
            AddNewSubsystem<NewAnimationSubsystem>();
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
            startButton.AddData(new Transform(0, 0, 128, 64, 0, 0, 0, Anchor.CENTRE));
            startButton.AddData(new CString("StartButton"));
            startButton.AddData(new Button(() => StartDemo()));

            var button = CObject.New();
            button.AddData(ExitTexture);
            button.AddData(new Transform(-128, 0, 128, 64, 0, 0, 0, Anchor.TOP_RIGHT));
            button.AddData(new Button(() => Engine.Stop()));
        }
        public void StartDemo()
        {
            CObject.Destroy(Objects.Get("StartButton"));
            //WObject.Destroy(Objects.Get("StartButton"));
#if GAME_TEST
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
            player.AddData(SquareAnimation);

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
            Objects.Iterate((ref Transform transform) =>
            {
                transform.Position += new Vector2f(1, 1);
            }, true);
        }
    }
#endif
    internal class NewAnimationSubsystem : AnimationSubsystem
    {
        public override void Update(float deltaSeconds)
        {
            Objects.Iterate((ref PlayerData player, ref Animation animation, ref Texture texture) =>
            {
                var increment = (int)( player.TotalHealth / animation.FrameCount );
                var position = ( player.TotalHealth - player.Health ) / increment;
                position = position == animation.FrameCount ? position - 1 : position;
                animation.SetTextureDataTo(position, ref texture);
            });

            Objects.Iterate((ref EnemyData enemy, ref Animation animation, ref Texture texture) =>
            {
                var increment = (int)( enemy.TotalHealth / animation.FrameCount );
                var position = ( enemy.TotalHealth - enemy.Health ) / increment;
                position = position == animation.FrameCount ? position - 1 : position;
                animation.SetTextureDataTo(position, ref texture);
            }, true);
        }
    }

    internal class PlayerSubsystem : Subsystem
    {
        //public WObject PlayerObject;
        //public WObject Health;
        //public WObject FPS;

        public CObject PlayerObject;
        public CObject Health;
        public CObject FPS;

        //public CObject PlayerObject;
        //public ByRefData<Text> Health;
        //public ByRefData<Text> FPS;
        public override void Startup()
        {
            base.Startup();
            PlayerObject = Objects.Get("Player 1");
            Health = Objects.Get("Health");
            FPS = Objects.Get("FPSCounter");
            //PlayerObject = Objects.Get("Player 1");
            //Health = Objects.GetDataRef<Text>("Health");
            //FPS = Objects.GetDataRef<Text>("FPSCounter");
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
            PlayerObject.WriteData((ref Transform transform, ref PlayerData player) =>
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

            /*Health.VolatileWrite((ref Text text) =>
            {
                text.ChangeText("Health: " + PlayerObject.GetData<PlayerData>().Health);
            });*/
            Health.WriteData((ref Text text) =>
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

            /*FPS.VolatileWrite((ref Text text) =>
            {
                text.ChangeText("FPS: " + Math.Floor(CTime.FPS));
            });*/
            FPS.WriteData((ref Text text) =>
            {
                text.ChangeText("FPS: " + Math.Floor(CTime.FPS));
            });
#endif
        }
    }
    internal class EnemySubsystem : Subsystem
    {
        public static int EnemyLimit = 250;
        public static int EnemyCount = 0;
        private int spawnAmount = 500;
        private float spawnTimer = 1f;
        //private WObject PlayerObject;
        private CObject PlayerObject;
        public override void Startup()
        {
            base.Startup();
            PlayerObject = Objects.Get("Player 1");
        }
        public override void Update(float deltaSeconds)
        {
            spawnTimer -= deltaSeconds;
            if(spawnTimer <= 0 && EnemyCount < EnemyLimit)
            {
                SpawnEnemy(spawnAmount);
                EnemyCount += spawnAmount;
                spawnTimer = 1f;
                /*Objects.GetDataRef<Text>("EnemyCount").VolatileWrite((ref Text text) =>
                {
                    text.ChangeText("Enemies: " + EnemyCount);
                });*/
                Objects.Get("EnemyCount").WriteData((ref Text text) =>
                {
                    text.ChangeText("Enemies: " + EnemyCount);
                });
            }
#if !PHYSICS_TEST
            //var PlayerTarget = Objects.GetData<Transform>("Player 1");
            var PlayerTarget = PlayerObject.GetData<Transform>();

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
                enemy.AddData(new EnemyData(10, 1, 100));
#if PHYSICS_TEST
                enemy.AddData(new PhysicsBody());
#endif
                enemy.AddData(new Collider());
                enemy.AddData(new CString("Enemy"));
                enemy.AddData(GameEngine.SquareAnimation);
            }
        }
    }
    internal class NewCollisionSubsystem : CollisionSubsystem
    {
        //public CObject PlayerObject;
        //public ByRefData<Text> KillCounter;
        //public WObject PlayerObject;
        public CObject PlayerObject;
        //public WObject KillCounter;
        public CObject KillCounter;

        public override void Startup()
        {
            base.Startup();
            //VerifyCollisions = true;
            PlayerObject = Objects.Get("Player 1");
            KillCounter = Objects.Get("KillCounter");
            //PlayerObject = Objects.Get("Player 1");
            //KillCounter = Objects.GetDataRef<Text>("KillCounter");
#if !PHYSICS_TEST
            Objects.IgnoreCollisions<EnemyData>();
#endif
        }
        public override void Update(float deltaSeconds)
        {
            base.Update(deltaSeconds);

            /*
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
                            //CObject.Destroy(ref PlayerObject);
                            //WObject.Destroy(PlayerObject);
                            CObject.Destroy(PlayerObject);
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
                if(hit.First() is CObject cObject && !cObject.IsNull)
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
                            //KillCounter.VolatileWrite((ref Text text) => text.ChangeText(kills));
                            KillCounter.WriteData((ref Text text) => text.ChangeText(kills));
                        }
                    });
                }
            }
            */

            if(GetCollisionsOf(PlayerObject) is List<CObject> list && list.Count > 0)
            {
                PlayerObject.WriteData((ref PlayerData data) =>
                {
                    foreach (var item in list)
                    {
                        var enemyData = item.GetData<EnemyData>();
                        if (enemyData.AttackCooldown <= 0)
                        {
                            data.Health -= enemyData.Damage;
                            item.WriteData((ref EnemyData data) => data.AttackCooldown = 1f);
                        }
                        else
                        {
                            item.WriteData((ref EnemyData data) => data.AttackCooldown -= deltaSeconds);
                        }

                        if (data.Health <= 0)
                        {
                            CObject.Destroy(PlayerObject);
                            return;
                        }
                    }
                });
            }

            Objects.IterateWithObject((ref BulletData bulletdata, ref CObject cObject) =>
            {
                var bullet = cObject;
                if(GetCollisionsOf(cObject) is List<CObject> list && list.Count > 0)
                {
                    var bdata = bulletdata;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var hit = list[i];
                        if (hit.IsNull)
                            continue;

                        hit.WriteData((ref EnemyData data) =>
                        {
                            data.Health -= bdata.Damage;
                            CObject.Destroy(bullet);

                            if (data.Health <= 0)
                            {
                                CObject.Destroy(hit);
                                EnemySubsystem.EnemyCount--;
                                var kills = "Kill count: ";
                                PlayerObject.WriteData((ref PlayerData player) =>
                                {
                                    ++player.KillCount;
                                    kills += player.KillCount;
                                });
                                //KillCounter.VolatileWrite((ref Text text) => text.ChangeText(kills));
                                KillCounter.WriteData((ref Text text) => text.ChangeText(kills));
                            }
                        });
                    }
                }
            });

            /*
            var bullets = GetPossibleCollisions.Where(x => x.Key.HasDataOf<BulletData>());
            foreach (var bullet in bullets)
            {
                var hit = bullet.Value.Where(x => x.HasDataOf<EnemyData>());
                var count = hit.Count();
                if (count == 0)
                    continue;
                if(hit.First() is CObject cObject && !cObject.IsNull)
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
                            //KillCounter.VolatileWrite((ref Text text) => text.ChangeText(kills));
                            KillCounter.WriteData((ref Text text) => text.ChangeText(kills));
                        }
                    });
                }
            }*/

        }
    }
    internal struct PlayerData : IComponentData
    {
        public int KillCount;
        public int TotalHealth;
        public int Health;
        public float Speed;
        public float AttackSpeed;
        public PlayerData(int health, float speed, float attackSpeed)
        {
            TotalHealth = health;
            Health = health;
            Speed = speed;
            KillCount = 0;
            AttackSpeed = attackSpeed;
        }
    }
    internal struct EnemyData : IComponentData
    {
        public int TotalHealth;
        public int Health;
        public int Damage;
        public float Speed;
        public float AttackSpeed;
        public float AttackCooldown;
        public EnemyData(int health, int dmg, float speed = 10, float attackSpeed = 1f)
        {
            TotalHealth = health;
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
