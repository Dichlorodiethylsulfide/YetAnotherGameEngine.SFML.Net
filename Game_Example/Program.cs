using ECS;
using ECS.Animations;
using ECS.Strings;
using ECS.Graphics;
using ECS.Library;
using ECS.Maths;
using ECS.Collision;
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
            Debug.Log("Hello World!");
            if (args.Length > 0)
            {
                var arg = args[0];
                if(arg == "--show-logger")
                {
                    ECS.Logger.Logger.ReadAvailableLogs();
                }
                else if(arg == "--help")
                {
                    Debug.Log("Commands:\n--help\tPrints this help string\n--set-flag\t Sets a single flag that can be checked while the engine is running\n--set-flags\tSets multiple flags, all string separated flags after '--set-flags' is included as a new flag\n--set-flag and --set-flags can include value setting with 'flag=integer' to assign a value within the engine.");
                }
                else if(arg == "--set-flag" && args.Length == 2)
                {
                    Engine.Start(typeof(GameEngine), args[1]);
                }
                else if(arg == "--set-flags" && args.Length > 2)
                {
                    var flags = args.Skip(2).ToArray();
                    Engine.Start(typeof(GameEngine), flags);
                }
                else
                {
                    Debug.Log("Unknown Arg! Type --help for additional commands.");
                }
            }
            else
            {
                Engine.Start(typeof(GameEngine));
            }
            Debug.Log("Goodbye World!");
        }
    }
    internal class GameEngine : Engine
    {
        public static bool IsRenderingTest = false;
        public static bool IsGameTest = false;

        public static CTexture SpriteSheetTexture;
        public static CTexture StartTexture;
        public static CTexture ExitTexture;
        public static CTexture SpawnTexture;
        public static CTexture BulletTexture;
        public static CAnimation SquareAnimation;
        public static CFont Arial;

        public static int SpawnCounter = 1;
        private static int SqSpawnCounter => SpawnCounter * SpawnCounter;

        public override EngineSettings Settings
        {
            get
            {
                if (IsRenderingTest)
                    return new EngineSettings(64, (int)( SqSpawnCounter * 1.1f ), (int)( SqSpawnCounter * 1.1f ), 64, new Vector2u(1280, 720), "Engine Window", false, false, false, 60);
                return new EngineSettings(64, 1024, 1024, 64, new Vector2u(1280, 720), "Engine Window", false, true, true, 60);
            }
        }

        public override void Initialise()
        {
            IsRenderingTest = IsEngineFlagSet("Rendering-Test");
            IsGameTest = IsEngineFlagSet("Game-Test") || !IsRenderingTest;

            if(IsEngineFlagSetWithValue("SpawnCount") is CTuple<bool, int> tuple && tuple.Item1)
            {
                SpawnCounter = tuple.Item2;
            }
            else
            {
                if(IsRenderingTest)
                {
                    SpawnCounter = 100;
                }
                else
                {
                    SpawnCounter = 1;
                }
            }

            SpriteSheetTexture = CTexture.CreateSpriteSheet(FilePath.Get(@".\Resources\Placeholder\Block_SpriteSheet.png"), new Vector2u(32, 32));
            StartTexture = new CTexture(FilePath.Get(@".\Resources\Placeholder\StartButton.png"));
            ExitTexture = new CTexture(FilePath.Get(@".\Resources\Placeholder\ExitButton.png"));
            BulletTexture = new CTexture(FilePath.Get(@".\Resources\Placeholder\Bullet.png"));
            Arial = new CFont(FilePath.Get(@".\Resources\Placeholder\arial.ttf"));

            SpawnTexture = SpriteSheetTexture.GetSpriteFromSheet(0);
            SquareAnimation = new CAnimation("SquareAnimation", 5);
            for (int i = 0; i < 5; i++)
                SquareAnimation.SetFrameDataTo(i, ref SpriteSheetTexture);
            if(IsGameTest)
            {
                AddNewSubsystem<NewCollisionSubsystem>();
                AddNewSubsystem<NewAnimationSubsystem>();
                AddNewSubsystem<PlayerSubsystem>();
                AddNewSubsystem<EnemySubsystem>();
                AddNewDataType<CPlayerData>();
                AddNewDataType<CEnemyData>();
                AddNewDataType<CBulletData>();
            }
            else
            {
                AddNewSubsystem<MovementSubsystem>();
            }

            var startButton = CObject.New();
            startButton.AddData(StartTexture);
            startButton.AddData(new CTransform(0, 0, 128, 64, 0, 0, 0, Anchor.CENTRE));
            startButton.AddData(new CString("StartButton"));
            startButton.AddData(new CButton(() => StartDemo()));

            var button = CObject.New();
            button.AddData(ExitTexture);
            button.AddData(new CTransform(-128, 0, 128, 64, 0, 0, 0, Anchor.TOP_RIGHT));
            button.AddData(new CButton(() => Stop()));
        }
        public void StartDemo()
        {
            CObject.Destroy(Objects.Get("StartButton"));
            if(IsGameTest)
            {
                var fpsCounter = CObject.New();
                fpsCounter.AddData(new CText("FPS: ", Arial, 20));
                fpsCounter.AddData(new CTransform(0, 60, 0, 0, 0, 0));
                fpsCounter.AddData(new CString("FPSCounter"));

                var enemyCount = CObject.New();
                enemyCount.AddData(new CText("Enemies: 0", Arial, 20));
                enemyCount.AddData(new CTransform(0, 40, 0, 0, 0, 0));
                enemyCount.AddData(new CString("EnemyCount"));

                var text = CObject.New();
                text.AddData(new CText("Health: NaN", Arial, 20));
                text.AddData(new CTransform(0, 20, 0, 0, 0, 0));
                text.AddData(new CString("Health"));

                var text2 = CObject.New();
                text2.AddData(new CText("Kill count: 0", Arial, 20));
                text2.AddData(new CTransform(0, 0, 0, 0, 0, 0));
                text2.AddData(new CString("KillCounter"));

                var player = CObject.New();
                player.AddData(SpawnTexture.Modify((ref CTexture texture) => texture.SetColor(Color.Blue)));
                player.AddData(new CTransform(100, 100, 32, 32));
                player.AddData(new CPlayerData(100, 100, 1f));
                player.AddData(new CCollider());
                player.AddData(SquareAnimation);
                player.AddData(new CString("Player 1"));
            }
            else
            {
                for (int y = 0; y < SpawnCounter; y++)
                {
                    for (int x = 0; x < SpawnCounter; x++)
                    {
                        var cObject = CObject.New();
                        cObject.AddData(new CTransform(x * 33, y * 33, 32, 32));
                        cObject.AddData(SpawnTexture);
                    }
                }
            }
            StartSubsystems();
        }
    }
    internal class MovementSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Objects.Iterate((ref CTransform transform) =>
            {
                transform.Position += new Vector2f(1, 1);
            }, true);
        }
    }
    internal class NewAnimationSubsystem : AnimationSubsystem
    {
        public override void Update(float deltaSeconds)
        {
            Objects.Iterate((ref CPlayerData player, ref CAnimation animation, ref CTexture texture) =>
            {
                if (player.Health < 20)
                    animation.SetTextureDataTo(4, ref texture);
                else if(player.Health < 40)
                    animation.SetTextureDataTo(3, ref texture);
                else if (player.Health < 60)
                    animation.SetTextureDataTo(2, ref texture);
                else if (player.Health < 80)
                    animation.SetTextureDataTo(1, ref texture);
                else
                    animation.SetTextureDataTo(0, ref texture);
            });

            Objects.Iterate((ref CEnemyData enemy, ref CAnimation animation, ref CTexture texture) =>
            {
                if (enemy.Health < 2)
                    animation.SetTextureDataTo(4, ref texture);
                else if (enemy.Health < 4)
                    animation.SetTextureDataTo(3, ref texture);
                else if (enemy.Health < 6)
                    animation.SetTextureDataTo(2, ref texture);
                else if (enemy.Health < 8)
                    animation.SetTextureDataTo(1, ref texture);
                else
                    animation.SetTextureDataTo(0, ref texture);
            });
        }
    }

    internal class PlayerSubsystem : Subsystem
    {
        public CObject PlayerObject;
        public CObject Health;
        public CObject FPS;
        public override void Startup()
        {
            base.Startup();
            PlayerObject = Objects.Get("Player 1");
            Health = Objects.Get("Health");
            FPS = Objects.Get("FPSCounter");
        }
        public override void Update(float deltaSeconds)
        {
            PlayerObject.WriteData((ref CTransform transform, ref CPlayerData player) =>
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
                    cBullet.AddData(new CTransform(transform.Position.X, transform.Position.Y, 12, 17, 6, 8.5f, direction.ToRotation()));
                    cBullet.AddData(new CBulletData(direction, 200f, 1));
                    cBullet.AddData(new CCollider());
                    player.AttackSpeed = 0.1f;
                }
            });
            Health.WriteData((ref CText text) =>
            {
                text.ChangeText("Health: " + PlayerObject.GetData<CPlayerData>().Health);
            });

            Objects.IterateWithObject((ref CBulletData bullet, ref CTransform transform, ref CObject cObject) =>
            {
                transform.Position += bullet.Direction * bullet.Speed * deltaSeconds;
                bullet.LifeTime -= deltaSeconds;
                if (bullet.LifeTime <= 0)
                {
                    CObject.Destroy(cObject);
                }
            });
            FPS.WriteData((ref CText text) =>
            {
                text.ChangeText("FPS: " + Math.Floor(ECS.Time.FPS));
            });
        }
    }
    internal class EnemySubsystem : Subsystem
    {
        public static int EnemyLimit = int.MaxValue;
        public static int EnemyCount = 0;
        private int spawnAmount = GameEngine.SpawnCounter;
        private float spawnTimer = 5f;
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
                Objects.Get("EnemyCount").WriteData((ref CText text) =>
                {
                    text.ChangeText("Enemies: " + EnemyCount);
                });
            }

            var PlayerTarget = PlayerObject.GetData<CTransform>();

            Objects.Iterate((ref CEnemyData enemy, ref CTransform transform) =>
            {
                var direction = (PlayerTarget.Position - transform.Position).Normalise();

                transform.Position += direction * enemy.Speed * deltaSeconds;
            });
        }

        public static void SpawnEnemy(int count = 1)
        {
            for(int i = 0; i < count; i++)
            {
                var enemy = CObject.New();
                enemy.AddData(GameEngine.SpawnTexture.Modify((ref CTexture texture) => texture.SetColor(Color.Red)));
                enemy.AddData(new CTransform(RNG.Next(0, (int)Engine.EngineWindow.WindowDimensions.X), RNG.Next(0, (int)Engine.EngineWindow.WindowDimensions.Y), 32, 32));
                enemy.AddData(new CEnemyData(10, 1, 50));
                enemy.AddData(new CCollider());
                enemy.AddData(new CString("Enemy"));
                if(GameEngine.IsGameTest)
                    enemy.AddData(GameEngine.SquareAnimation);
            }
        }
    }
    internal class NewCollisionSubsystem : CollisionSubsystem
    {
        public CObject PlayerObject;
        public CObject KillCounter;

        public override void Startup()
        {
            base.Startup();
            PlayerObject = Objects.Get("Player 1");
            KillCounter = Objects.Get("KillCounter");
        }
        public override void Update(float deltaSeconds)
        {
            base.Update(deltaSeconds);

            if(GetCollisionsOf<CEnemyData>(PlayerObject) is List<CObject> list && list.Count > 0)
            {
                PlayerObject.WriteData((ref CPlayerData data) =>
                {
                    for(var i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        var enemyData = item.GetData<CEnemyData>();
                        if (enemyData.AttackCooldown <= 0)
                        {
                            data.Health -= enemyData.Damage;
                            item.WriteData((ref CEnemyData data) => data.AttackCooldown = 1f);
                        }
                        else
                        {
                            item.WriteData((ref CEnemyData data) => data.AttackCooldown -= deltaSeconds);
                        }

                        if (data.Health <= 0)
                        {
                            CObject.Destroy(PlayerObject);
                            return;
                        }
                    }
                });
            }

            Objects.IterateWithObject((ref CBulletData bulletdata, ref CObject cObject) =>
            {
                var hit = GetFirstCollisionOf<CEnemyData>(cObject);
                if (!hit.IsNull)
                {
                    var health = hit.GetData<CEnemyData>().Health - bulletdata.Damage;
                    if (health <= 0)
                    {
                        CObject.Destroy(hit);
                        EnemySubsystem.EnemyCount--;
                        var kills = "Kill count: ";
                        PlayerObject.WriteData((ref CPlayerData player) =>
                        {
                            ++player.KillCount;
                            kills += player.KillCount;
                        });
                        KillCounter.WriteData((ref CText text) => text.ChangeText(kills));
                    }
                    else
                        hit.WriteData((ref CEnemyData data) => data.Health = health);
                    CObject.Destroy(cObject);
                }
            });
        }
    }
    internal struct CPlayerData : ICData
    {
        public int KillCount;
        public int TotalHealth;
        public int Health;
        public float Speed;
        public float AttackSpeed;
        public CPlayerData(int health, float speed, float attackSpeed)
        {
            TotalHealth = health;
            Health = health;
            Speed = speed;
            KillCount = 0;
            AttackSpeed = attackSpeed;
        }
    }
    internal struct CEnemyData : ICData
    {
        public int TotalHealth;
        public int Health;
        public int Damage;
        public float Speed;
        public float AttackSpeed;
        public float AttackCooldown;
        public CEnemyData(int health, int dmg, float speed = 10, float attackSpeed = 1f)
        {
            TotalHealth = health;
            Health = health;
            Damage = dmg;
            Speed = speed;
            AttackSpeed = attackSpeed;
            AttackCooldown = 1f;
        }
    }
    public struct CBulletData : ICData
    {
        public int Damage;
        public Vector2f Direction;
        public float Speed;
        public float LifeTime;
        public CBulletData(Vector2f dir, float speed, int dmg = 10)
        {
            Damage = dmg;
            Direction = dir;
            Speed = speed;
            LifeTime = 3f;
        }
    }
}
