#define GAME_TEST
//#undef GAME_TEST
#define BRUTE_FORCE
#undef BRUTE_FORCE
#define BRUTE_FORCE_RENDERING
#undef BRUTE_FORCE_RENDERING
#define PhsicsTest
#undef PhysicsTest

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
        private const string SpawnButton = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\SpawnButton.png";
        private const string Placeholder = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block 2.png";

        public static Texture BulletTexture;
        public static ECS.UI.Font Arial;
        public override EngineSettings Settings => new EngineSettings(64, 1024000, 1024000, 64, new Vector2u(1280, 720), "Engine Window", false, true, true);
        public override void Initialise()
        {
            var texture = new Texture(Placeholder);
#if GAME_TEST
            AddNewSubsystem<NewCollisionSubsystem>();
            AddNewSubsystem<PlayerSubsystem>();
            AddNewSubsystem<EnemySubsystem>();
            AddNewDataType<PlayerData>();
            AddNewDataType<EnemyData>();
            AddNewDataType<BulletData>();

            var buttonTexture = new Texture(ExitButton);
            var spawnTexture = new Texture(SpawnButton);
            BulletTexture = new Texture(Bullet);
            Arial = new ECS.UI.Font(ArialFont);

            var button = CObject.New();
            button.AddData(buttonTexture);
            button.AddData(new Transform(1152, 0, 128, 64, 0, 0));
            button.AddData(new Button(() => Engine.Stop()));

            var spawn = CObject.New();
            spawn.AddData(spawnTexture);
            spawn.AddData(new Transform(1152, 64, 128, 64, 0, 0));
            spawn.AddData(new Button(() => {
                var enemy = CObject.New();
                enemy.AddData(texture.SetModifiedTextureColor(Color.Red));
                enemy.AddData(new Transform(Random.Next(0, (int)Settings.WindowDimensions.X), Random.Next(0, (int)Settings.WindowDimensions.Y), 32, 32));
                enemy.AddData(new EnemyData(10, 10, 100));
                enemy.AddData(new Collider());
                enemy.AddData(new CString("Enemy"));
            }));

            var text = CObject.New();
            text.AddData(new ECS.UI.Text("Health: NaN", Arial, 20));
            text.AddData(new Transform(0, 20, 0, 0, 0, 0));
            text.AddData(new CString("Health"));

            var text2 = CObject.New();
            text2.AddData(new ECS.UI.Text("Kill count: 0", Arial, 20));
            text2.AddData(new Transform(0, 0, 0, 0, 0, 0));
            text2.AddData(new CString("KillCounter"));

            var player = CObject.New();
            player.AddData(texture.SetModifiedTextureColor(Color.Blue));
            player.AddData(new Transform(100, 100, 32, 32));
            player.AddData(new PlayerData(100, 100));
            player.AddData(new Collider());
            player.AddData(new CString("Player 1"));
            
#elif BRUTE_FORCE || BRUTE_FORCE_RENDERING
            var counter = 1000;
            for(int y = 0; y < counter; y++)
            {
                for(int x = 0; x < counter; x++)
                {
                    var cObject = CObject.New();
                    cObject.AddData(new Transform(x * 33, y * 33, 32, 32));
#if BRUTE_FORCE_RENDERING
                    cObject.AddData(texture);
#else
                    cObject.AddData(texture.CalculateShouldDraw(cObject.GetData<Transform>().BoundingBox, EngineWindow.GetWindowBoundingBox));
#endif
                }
            }
#endif
                }
            }
    internal class PlayerSubsystem : Subsystem
    {
        public CObject PlayerObject;
        public ByRef<ECS.UI.Text> Health;
        public override void Startup()
        {
            PlayerObject = Entities.Get("Player 1");
            Health = Entities.GetRef<ECS.UI.Text>("Health");
        }
        public override void Update(float deltaSeconds)
        {
            PlayerObject.WriteData((ref Transform transform) =>
            {
                var player = PlayerObject.GetData<PlayerData>();
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
                if (Input.GetMouseButtonPressed(0))
                {
                    var direction = transform.Position.Direction(Input.GetMousePosition());
                    var cBullet = CObject.New();
                    cBullet.AddData(GameEngine.BulletTexture);
                    cBullet.AddData(new Transform(transform.Position.X, transform.Position.Y, 12, 17, 6, 8.5f, direction.ToRotation()));
                    cBullet.AddData(new BulletData(direction, 200f, 1));
                    cBullet.AddData(new Collider());
                }
            });
            Health.VolatileWrite((ref ECS.UI.Text text) =>
            {
                text.ChangeText("Health: " + PlayerObject.GetData<PlayerData>().Health);
            });
            
            Entities.IterateWithObject((ref BulletData bullet, ref Transform transform, ref CObject cObject) =>
            {
                transform.Position += bullet.Direction * bullet.Speed * deltaSeconds;
                bullet.LifeTime -= deltaSeconds;
                if(bullet.LifeTime <= 0)
                {
                    CObject.Destroy(cObject);
                }
            });    
        }
    }
    internal class EnemySubsystem : Subsystem
    {
        //public Transform PlayerTarget;
        public override void Update(float deltaSeconds)
        {
            //Entities.Iterate((ref PlayerData player, ref Transform transform) => { PlayerTarget = transform; });
            var PlayerTarget = Entities.Get<Transform>("Player 1");

            Entities.Iterate((ref EnemyData enemy, ref Transform transform) =>
            {
                var direction = (PlayerTarget.Position - transform.Position).Normalise();

                transform.Position += direction * enemy.Speed * deltaSeconds;
            });
        }
    }
    internal class NewCollisionSubsystem : CollisionSubsystem
    {
        public CObject PlayerObject;
        public ByRef<ECS.UI.Text> KillCounter;

        public override void Startup()
        {
            VerifyCollisions = true;
            PlayerObject = Entities.Get("Player 1");
            KillCounter = Entities.GetRef<ECS.UI.Text>("KillCounter");
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

            var bullets = GetPossibleCollisions.Where(x => x.Key.HasDataOf<BulletData>());//GetVerifiedCollisions.Where(x => x.Key.HasDataOf<BulletData>());
            foreach (var bullet in bullets)
            {
                var hit = bullet.Value.Where(x => x.HasDataOf<EnemyData>());
                var count = hit.Count();
                if (count == 0)
                    continue;
                hit.First().WriteData((ref EnemyData data) =>
                {
                    data.Health -= bullet.Key.GetData<BulletData>().Damage;
                    CObject.Destroy(bullet.Key);
                    if (data.Health <= 0)
                    {
                        CObject.Destroy(hit.First());
                        var kills = "Kill count: ";
                        PlayerObject.WriteData((ref PlayerData player) =>
                        {
                            ++player.KillCount;
                            kills += player.KillCount;
                        });
                        KillCounter.VolatileWrite((ref ECS.UI.Text text) => text.ChangeText(kills));
                    }
                });
            }
            
        }
    }
    internal struct PlayerData : IComponentData
    {
        public int KillCount;
        public int Health;
        public float Speed;
        public PlayerData(int health, float speed)
        {
            Health = health;
            Speed = speed;
            KillCount = 0;
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

}
