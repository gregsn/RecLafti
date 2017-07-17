using System;
using System.Collections.Generic;
using FeralTic.DX11;
using SlimDX;
using SlimDX.Direct3D11;
using VVVV.PluginInterfaces.V2;

namespace RecLafti
{
    #pragma warning disable CS0649 //MEF => unassigned fields e.g. [Input] or [Output] => ignore warnings

    public class Field<T>
    {
        public T Value { get; private set; }
        public bool Changed;

        public bool HasChanged(T newValue)
        {
            Changed = !newValue?.Equals(Value) ?? !Value?.Equals(newValue) ?? false;
            Value = newValue;
            return Changed;
        }
    }

    public static class FieldHelper
    {
        public static bool Changed<T>(this Field<T> field, ISpread<T> pin)
            where T : IShaderNode
            => pin.SliceCount > 0 && (field.HasChanged(pin[0]) || (pin[0]?.HasChanged ?? false));
    }

    public abstract class GlobalVarNode<T> : IGlobalVarNode<T>, IPluginEvaluate
    {
        [Input("Var Name")]
        ISpread<string> Name;
        Field<string> NameField = new Field<string>();

        [Input("Default Value")]
        ISpread<T> DefaultValue;

        [Output("Output")]
        ISpread<IGlobalVarNode<T>> Output;

        public bool HasChanged => NameField.HasChanged(Name[0]);

        public VarDeclaration GetGlobalVar(string[] reserved) 
            => new VarDeclaration(ShaderGraph.GetTypeName<T>(), Name[0].GetUniqueName(reserved), DefaultValue[0].GetHLSLValue());

        public void Evaluate(int SpreadMax)
        {
            Output.SliceCount = 1;
            Output[0] = this;
        }

        public void SetEffectValue(EffectVariable sv, int i) => EffectVariableHelpers.Set(sv, (dynamic)DefaultValue[i]);
    }

    [PluginInfo(Name = "Float", Category = ShaderGraph.Category)]
    public class FloatNode : GlobalVarNode<float>
    {
    }

    [PluginInfo(Name = "Vector2", Category = ShaderGraph.Category)]
    public class Vector2Node : GlobalVarNode<Vector2>
    {
    }

    [PluginInfo(Name = "Vector3", Category = ShaderGraph.Category)]
    public class Vector3Node : GlobalVarNode<Vector3>
    {
    }

    [PluginInfo(Name = "Vector4", Category = ShaderGraph.Category)]
    public class Vector4Node : GlobalVarNode<Vector4>
    {
    }

    [PluginInfo(Name = "Color", Category = ShaderGraph.Category)]
    public class ColorNode : IGlobalVarNode<Vector4>, IPluginEvaluate
    {
        [Input("Var Name")]
        ISpread<string> Name;
        Field<string> NameField = new Field<string>();

        [Input("Default Value")]
        ISpread<Color4> DefaultValue;

        [Output("Output")]
        ISpread<IGlobalVarNode<Vector4>> Output;

        public bool HasChanged => NameField.HasChanged(Name[0]);

        public VarDeclaration GetGlobalVar(string[] reserved)
        {
            var name = Name[0].GetUniqueName(reserved); 
            return new VarDeclaration(ShaderGraph.GetTypeName<Vector4>(), name, DefaultValue[0].GetHLSLValue(), $"bool color=true; String uiname=\"{name}\";");
        }

        public void Evaluate(int SpreadMax)
        {
            Output.SliceCount = 1;
            Output[0] = this;
        }

        public void SetEffectValue(EffectVariable sv, int i) => EffectVariableHelpers.Set(sv, DefaultValue[0]);
    }

    public abstract class ShaderNodeBase<T> : IFunctionNode<T>
    {
        [Output("Output")]
        ISpread<IFunctionNode<T>> Output;

        public abstract IEnumerable<IShaderNode> NodeArguments { get; }
        public abstract bool HasChanged { get; }

        public readonly string Name;
        string IFunctionNode.Name => Name;

        public readonly string Code;
        string IFunctionNode.Code => Code;

        public ShaderNodeBase(string name, string code)
        {
            Name = name;
            Code = code;
        }

        public virtual VarDeclaration CreateShaderCallAssignment(string localVar, IEnumerable<string> arguments)
            => new VarDeclaration(ShaderGraph.GetTypeName<T>(), localVar, $"{Name}({string.Join(", ", arguments)})");

        public void Evaluate(int SpreadMax)
        {
            Output.SliceCount = 1;
            Output[0] = this;
        }        
    }

    // http://www.iquilezles.org/www/articles/distfunctions/distfunctions.htm

    [PluginInfo(Name = "Circular", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Circular2DDistanceFieldNode : ShaderNodeBase<float>, IPluginEvaluate
    {
        [Input("Center")]
        ISpread<IShaderNode<Vector2>> Center;
        IShaderNode<Vector2> CenterDefault = new Default<Vector2>(nameof(Center));
        Field<IShaderNode<Vector2>> CenterField = new Field<IShaderNode<Vector2>>();

        [Input("Radius")]
        ISpread<IShaderNode<float>> Radius;
        IShaderNode<float> RadiusDefault = new Default<float>(nameof(Radius), 0.2f);
        Field<IShaderNode<float>> RadiusField = new Field<IShaderNode<float>>();

        static IMadeUpShaderNode TexCdIn = new MadeUpShaderNode("In.TexCd");

        const string name = "Circular2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return Center[0] ?? CenterDefault;
                yield return Radius[0] ?? RadiusDefault;
                yield return TexCdIn;
            }
        }

        public override bool HasChanged => CenterField.Changed(Center) || RadiusField.Changed(Radius);

        public Circular2DDistanceFieldNode()
            : base(
                  name: name,
                  code: $@"
float {name}(float2 center, float radius, float2 samplePos)
{{
    return length(samplePos - center) - radius;
}}")
        {
        }
    }

    [PluginInfo(Name = "Quadratic", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Quadratic2DDistanceFieldNode : ShaderNodeBase<float>, IPluginEvaluate
    {
        [Input("Center")]
        ISpread<IShaderNode<Vector2>> Center;
        IShaderNode<Vector2> CenterDefault = new Default<Vector2>(nameof(Center));
        Field<IShaderNode<Vector2>> CenterField = new Field<IShaderNode<Vector2>>();

        [Input("Radius")]
        ISpread<IShaderNode<float>> Radius;
        IShaderNode<float> RadiusDefault = new Default<float>(nameof(Radius), 0.2f);
        Field<IShaderNode<float>> RadiusField = new Field<IShaderNode<float>>();

        static IMadeUpShaderNode TexCdIn = new MadeUpShaderNode("In.TexCd");

        const string name = "Quadratic2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return Center[0] ?? CenterDefault;
                yield return Radius[0] ?? RadiusDefault;
                yield return TexCdIn;
            }
        }

        public override bool HasChanged => CenterField.Changed(Center) || RadiusField.Changed(Radius);

        public Quadratic2DDistanceFieldNode()
            : base(
                  name: name,
                  code: $@"
float {name}(float2 center, float radius, float2 samplePos)
{{
    float2 d = abs(samplePos - center) - radius;
    return max(d.x, d.y); 
}}")
        {
        }
    }

    [PluginInfo(Name = "DF2DToColor", Category = ShaderGraph.Category)]
    public class Colorize2DDistanceField : ShaderNodeBase<Vector4>, IPluginEvaluate
    {
        [Input("Distance Field")]
        ISpread<IShaderNode<float>> DF;
        IShaderNode<float> DFDefault = new Default<float>(nameof(DF));
        Field<IShaderNode<float>> DFField = new Field<IShaderNode<float>>();

        [Input("Color")]
        ISpread<IShaderNode<Vector4>> Color;
        IShaderNode<Vector4> ColorDefault = new Default<Vector4>(nameof(Color), new Vector4(0.21f, 0.65f, 0f, 1f));
        Field<IShaderNode<Vector4>> ColorField = new Field<IShaderNode<Vector4>>();

        [Input("Background Color")]
        ISpread<IShaderNode<Vector4>> BGColor;
        IShaderNode<Vector4> BGColorDefault = new Default<Vector4>(nameof(BGColor), new Vector4(0.05f, 0.05f, 0.05f, 0.8f));
        Field<IShaderNode<Vector4>> BGColorField = new Field<IShaderNode<Vector4>>();

        const string name = "Colorize2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return DF[0] ?? DFDefault;
                yield return Color[0] ?? ColorDefault;
                yield return BGColor[0] ?? BGColorDefault;
            }
        }

        public override bool HasChanged => DFField.Changed(DF) || ColorField.Changed(Color) || BGColorField.Changed(BGColor);

        public Colorize2DDistanceField()
            : base(
                  name: name,
                  code: $@"
float4 {name}(float d, float4 color, float4 backcolor)
{{
    return lerp(color, backcolor, saturate(d * 500));
}}")
        {
        }
    }

    [PluginInfo(Name = "IsInShape", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class ISIn2DDistanceField : ShaderNodeBase<float>, IPluginEvaluate
    {
        [Input("Distance Field")]
        ISpread<IShaderNode<float>> DF;
        IShaderNode<float> DFDefault = new Default<float>(nameof(DF));
        Field<IShaderNode<float>> DFField = new Field<IShaderNode<float>>();

        const string name = "IsIn2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return DF[0] ?? DFDefault;
            }
        }

        public override bool HasChanged => DFField.Changed(DF);

        public ISIn2DDistanceField()
            : base(
                  name: name,
                  code: $@"
float {name}(float d)
{{
    return saturate(-d * 500);
}}")
        {
        }
    }

    [PluginInfo(Name = "Blend4Scalars", Category = ShaderGraph.Category)]
    public class BlendPerChannel : ShaderNodeBase<Vector4>, IPluginEvaluate
    {
        [Input("Color")]
        ISpread<IShaderNode<Vector4>> Color;
        IShaderNode<Vector4> ColorDefault = new Default<Vector4>(nameof(Color), new Vector4(0.21f, 0.65f, 0f, 1f));
        Field<IShaderNode<Vector4>> ColorField = new Field<IShaderNode<Vector4>>();

        [Input("Color 2")]
        ISpread<IShaderNode<Vector4>> Color2;
        IShaderNode<Vector4> Color2Default = new Default<Vector4>(nameof(Color2), new Vector4(0.05f, 0.05f, 0.05f, 0.8f));
        Field<IShaderNode<Vector4>> Color2Field = new Field<IShaderNode<Vector4>>();

        [Input("Blend Per Channel")]
        ISpread<IShaderNode<Vector4>> BlendFactor;
        IShaderNode<Vector4> BlendDefault = new Default<Vector4>(nameof(BlendFactor), new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
        Field<IShaderNode<Vector4>> BlendField = new Field<IShaderNode<Vector4>>();

        const string name = "Blend4Scalars";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return Color[0] ?? ColorDefault;
                yield return Color2[0] ?? Color2Default;
                yield return BlendFactor[0] ?? BlendDefault;
            }
        }

        public override bool HasChanged => ColorField.Changed(Color) || Color2Field.Changed(Color2) || BlendField.Changed(BlendFactor);

        public BlendPerChannel()
            : base(
                  name: name,
                  code: $@"
float4 {name}(float4 color, float4 color2, float4 blend)
{{
    return lerp(color, color2, blend);
}}")
        {
        }
    }

    [PluginInfo(Name = "Blend", Category = ShaderGraph.Category)]
    public class Blend : ShaderNodeBase<Vector4>, IPluginEvaluate
    {
        [Input("Color")]
        ISpread<IShaderNode<Vector4>> Color;
        IShaderNode<Vector4> ColorDefault = new Default<Vector4>(nameof(Color), new Vector4(0.21f, 0.65f, 0f, 1f));
        Field<IShaderNode<Vector4>> ColorField = new Field<IShaderNode<Vector4>>();

        [Input("Color 2")]
        ISpread<IShaderNode<Vector4>> Color2;
        IShaderNode<Vector4> Color2Default = new Default<Vector4>(nameof(Color2), new Vector4(0.05f, 0.05f, 0.05f, 0.8f));
        Field<IShaderNode<Vector4>> Color2Field = new Field<IShaderNode<Vector4>>();

        [Input("Blend")]
        ISpread<IShaderNode<float>> BlendFactor;
        IShaderNode<float> BlendDefault = new Default<float>(nameof(BlendFactor), 0.0f);
        Field<IShaderNode<float>> BlendField = new Field<IShaderNode<float>>();

        const string name = "Blend";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return Color[0] ?? ColorDefault;
                yield return Color2[0] ?? Color2Default;
                yield return BlendFactor[0] ?? BlendDefault;
            }
        }

        public override bool HasChanged => ColorField.Changed(Color) || Color2Field.Changed(Color2) || BlendField.Changed(BlendFactor);

        public Blend()
            : base(
                  name: name,
                  code: $@"
float4 {name}(float4 color, float4 color2, float blend)
{{
    return lerp(color, color2, blend);
}}")
        {
        }
    }

    [PluginInfo(Name = "Debug", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Debug2DDistanceField : ShaderNodeBase<Vector4>, IPluginEvaluate
    {
        [Input("Distance Field")]
        ISpread<IShaderNode<float>> DF;
        IShaderNode<float> DFDefault = new Default<float>(nameof(DF));
        Field<IShaderNode<float>> DFField = new Field<IShaderNode<float>>();

        const string name = "Debug2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return DF[0] ?? DFDefault;
            }
        }

        public override bool HasChanged => DFField.Changed(DF);

        public Debug2DDistanceField()
            : base(
                  name: name,
                  code: $@"
float4 {name}(float d)
{{
    return float4(d, d, d, 1);
}}")
        {
        }
    }

    public abstract class Binary2DDistanceFieldOperator : ShaderNodeBase<float>, IPluginEvaluate
    {
        IShaderNode<float> DFDefault = new Default<float>(nameof(DF));

        [Input("Input")]
        ISpread<IShaderNode<float>> DF;
        Field<IShaderNode<float>> DFField = new Field<IShaderNode<float>>();

        [Input("Input 2")]
        ISpread<IShaderNode<float>> DF2;
        Field<IShaderNode<float>> DFField2 = new Field<IShaderNode<float>>();

        const string name = "Debug2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return DF[0] ?? DFDefault;
                yield return DF2[0] ?? DFDefault;
            }
        }

        public override bool HasChanged => DFField.Changed(DF) || DFField2.Changed(DF2);

        public Binary2DDistanceFieldOperator(string name, string code) : base(name, code) { }
    }

    [PluginInfo(Name = "Unite", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Union2DDistanceFieldOperator : Binary2DDistanceFieldOperator
    {
        const string name = "Unite2DDF";

        public Union2DDistanceFieldOperator()
            : base(
                  name: name,
                  code: $@"
float {name}(float d, float d2)
{{
    return min(d, d2);
}}")
        {
        }
    }

    [PluginInfo(Name = "Substract", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Substract2DDistanceFieldOperator : Binary2DDistanceFieldOperator
    {
        const string name = "Substract2DDF";

        public Substract2DDistanceFieldOperator()
            : base(
                  name: name,
                  code: $@"
float {name}(float d, float d2)
{{
    return max(d, -d2);
}}")
        {
        }
    }

    [PluginInfo(Name = "Intersect", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Intersect2DDistanceFieldOperator : Binary2DDistanceFieldOperator
    {
        const string name = "Union2DDF";

        public Intersect2DDistanceFieldOperator()
            : base(
                  name: name,
                  code: $@"
float {name}(float d, float d2)
{{
    return max(d, d2);
}}")
        {
        }
    }

    [PluginInfo(Name = "Outline", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Outline2DDistanceFieldNode : ShaderNodeBase<float>, IPluginEvaluate
    {
        IShaderNode<float> DFDefault = new Default<float>(nameof(DF));

        [Input("Input")]
        ISpread<IShaderNode<float>> DF;
        Field<IShaderNode<float>> DFField = new Field<IShaderNode<float>>();

        [Input("Thickness")]
        ISpread<IShaderNode<float>> Thickness;
        IShaderNode<float> ThicknessDefault = new Default<float>(nameof(Thickness), 0.02f);
        Field<IShaderNode<float>> ThicknessField = new Field<IShaderNode<float>>();

        const string name = "Outline2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return DF[0] ?? DFDefault;
                yield return Thickness[0] ?? ThicknessDefault;
            }
        }

        public override bool HasChanged => DFField.Changed(DF) || ThicknessField.Changed(Thickness);

        public Outline2DDistanceFieldNode()
            : base(
                  name: name,
                  code: $@"
float {name}(float d, float radius)
{{
    return abs(d) - radius;
}}")
        {
        }
    }

    [PluginInfo(Name = "Grow", Category = ShaderGraph.Category, Version = ShaderGraph.DistanceField2DVersion)]
    public class Grow2DDistanceFieldNode : ShaderNodeBase<float>, IPluginEvaluate
    {
        IShaderNode<float> DFDefault = new Default<float>(nameof(DF));

        [Input("Input")]
        ISpread<IShaderNode<float>> DF;
        Field<IShaderNode<float>> DFField = new Field<IShaderNode<float>>();

        [Input("Delta")]
        ISpread<IShaderNode<float>> Delta;
        IShaderNode<float> DeltaDefault = new Default<float>(nameof(Delta), 0.05f);
        Field<IShaderNode<float>> DeltaField = new Field<IShaderNode<float>>();

        const string name = "Grow2DDF";

        public override IEnumerable<IShaderNode> NodeArguments
        {
            get
            {
                yield return DF[0] ?? DFDefault;
                yield return Delta[0] ?? DeltaDefault;
            }
        }

        public override bool HasChanged => DFField.Changed(DF) || DeltaField.Changed(Delta);

        public Grow2DDistanceFieldNode()
            : base(
                  name: name,
                  code: $@"
float {name}(float d, float radius)
{{
    return d - radius;
}}")
        {
        }
    }
}
