using SFML.System;
using SFML.Graphics;
using System;

namespace ECS.Library
{
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
            myOrigin = new Vector2f();
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
    public struct Texture : Drawable, IComponentData
    {
        internal IntRect TextureRect;
        internal RenderWindow.Vertex4 Vertices;
        internal FloatRect InternalLocalBounds;
        internal IntPtr CTexture;
        public Texture(string filename)
        {
            TextureRect = new IntRect();
            CTexture = SFML.Graphics.Texture.sfTexture_createFromFile(filename, ref TextureRect);
            Vertices = new RenderWindow.Vertex4();
            InternalLocalBounds = new FloatRect();
            UpdateTexture(CTexture);
        }
        internal void UpdateRect()
        {
            var size = SFML.Graphics.Texture.sfTexture_getSize(CTexture);
            this.TextureRect = new IntRect(0, 0, Convert.ToInt32(size.X), Convert.ToInt32(size.Y));
            this.InternalLocalBounds = new FloatRect(0, 0, this.TextureRect.Width, this.TextureRect.Height);
        }
        public void UpdateTexture(IntPtr texture)
        {
            if (texture != IntPtr.Zero)
            {
                this.CTexture = texture;
                this.UpdateRect();

                var bounds = this.InternalLocalBounds;
                var left = Convert.ToSingle(this.TextureRect.Left);
                var right = left + this.TextureRect.Width;
                var top = Convert.ToSingle(this.TextureRect.Top);
                var bottom = top + this.TextureRect.Height;

                Vertices.Vertex0 = new Vertex(new Vector2f(0, 0), Color.White, new Vector2f(left, top));
                Vertices.Vertex1 = new Vertex(new Vector2f(0, bounds.Height), Color.White, new Vector2f(left, bottom));
                Vertices.Vertex2 = new Vertex(new Vector2f(bounds.Width, 0), Color.White, new Vector2f(right, top));
                Vertices.Vertex3 = new Vertex(new Vector2f(bounds.Width, bounds.Height), Color.White, new Vector2f(right, bottom));
            }
        }

        public void Draw(RenderTarget target, RenderStates states)
        {
            states.CTexture = CTexture;
            ((RenderWindow)target).Draw(Vertices, 0, 4, PrimitiveType.TriangleStrip, states);
        }
    }
}