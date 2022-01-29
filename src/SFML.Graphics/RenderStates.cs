using System;
using System.Runtime.InteropServices;

namespace SFML.Graphics
{
    ////////////////////////////////////////////////////////////
    /// <summary>
    /// Define the states used for drawing to a RenderTarget
    /// </summary>
    ////////////////////////////////////////////////////////////
    public struct RenderStates
    {
        ////////////////////////////////////////////////////////////
        /// <summary>
        /// Construct a default set of render states with an IntPtr for direct texture passing to C
        /// </summary>
        /// <param name="cTexture">C# IntPtr to pass to C</param>
        ////////////////////////////////////////////////////////////
        public RenderStates(IntPtr cTexture) :
            this(BlendMode.Alpha, SFMLTransform.Identity, null, null, cTexture)
        {
        }

        ////////////////////////////////////////////////////////////
        /// <summary>
        /// Construct a default set of render states with a custom blend mode
        /// </summary>
        /// <param name="blendMode">Blend mode to use</param>
        ////////////////////////////////////////////////////////////
        public RenderStates(BlendMode blendMode) :
            this(blendMode, SFMLTransform.Identity, null, null, IntPtr.Zero)
        {
        }

        ////////////////////////////////////////////////////////////
        /// <summary>
        /// Construct a default set of render states with a custom transform
        /// </summary>
        /// <param name="transform">Transform to use</param>
        ////////////////////////////////////////////////////////////
        public RenderStates(SFMLTransform transform) :
            this(BlendMode.Alpha, transform, null, null, IntPtr.Zero)
        {
        }

        ////////////////////////////////////////////////////////////
        /// <summary>
        /// Construct a default set of render states with a custom texture
        /// </summary>
        /// <param name="texture">Texture to use</param>
        ////////////////////////////////////////////////////////////
        public RenderStates(Texture texture) :
            this(BlendMode.Alpha, SFMLTransform.Identity, texture, null, IntPtr.Zero)
        {
        }

        ////////////////////////////////////////////////////////////
        /// <summary>
        /// Construct a default set of render states with a custom shader
        /// </summary>
        /// <param name="shader">Shader to use</param>
        ////////////////////////////////////////////////////////////
        public RenderStates(Shader shader) :
            this(BlendMode.Alpha, SFMLTransform.Identity, null, shader, IntPtr.Zero)
        {
        }

        ////////////////////////////////////////////////////////////
        /// <summary>
        /// Construct a set of render states with all its attributes
        /// </summary>
        /// <param name="blendMode">Blend mode to use</param>
        /// <param name="transform">Transform to use</param>
        /// <param name="texture">Texture to use</param>
        /// <param name="shader">Shader to use</param>
        ////////////////////////////////////////////////////////////
        public RenderStates(BlendMode blendMode, SFMLTransform transform, Texture texture, Shader shader, IntPtr cTexture)
        {
            BlendMode = blendMode;
            Transform = transform;
            Texture = texture;
            Shader = shader;
            CTexture = cTexture;
        }

        ////////////////////////////////////////////////////////////
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="copy">States to copy</param>
        ////////////////////////////////////////////////////////////
        public RenderStates(RenderStates copy)
        {
            BlendMode = copy.BlendMode;
            Transform = copy.Transform;
            Texture = copy.Texture;
            Shader = copy.Shader;
            CTexture = copy.CTexture;
        }

        ////////////////////////////////////////////////////////////
        /// <summary>Special instance holding the default render states</summary>
        ////////////////////////////////////////////////////////////
        public static RenderStates Default
        {
            get { return new RenderStates(BlendMode.Alpha, SFMLTransform.Identity, null, null, IntPtr.Zero); }
        }

        /// <summary>Blending mode</summary>
        public BlendMode BlendMode;

        /// <summary>Transform</summary>
        public SFMLTransform Transform;

        /// <summary>Texture</summary>
        public Texture Texture;

        /// <summary>CTexture</summary>
        public IntPtr CTexture;

        /// <summary>Shader</summary>
        public Shader Shader;

        // Return a marshalled version of the instance, that can directly be passed to the C API
        internal MarshalData Marshal()
        {
            MarshalData data = new MarshalData();
            data.blendMode = BlendMode;
            data.transform = Transform;
            data.texture = CTexture != IntPtr.Zero ? CTexture : Texture != null ? Texture.CPointer : IntPtr.Zero;//Texture != null ? Texture.CPointer : IntPtr.Zero;
            data.shader = Shader != null ? Shader.CPointer : IntPtr.Zero;

            return data;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MarshalData
        {
            public BlendMode blendMode;
            public SFMLTransform transform;
            public IntPtr texture;
            public IntPtr shader;
        }
    }
}
