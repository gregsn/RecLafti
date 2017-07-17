#region usings
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using FeralTic.DX11;
using System.Linq;
using VVVV.DX11.Lib.Effects;
using System;
using System.Collections.Generic;
using VVVV.DX11.Lib.Effects.Registries;
using SlimDX;
using VVVV.DX11.Internals.Effects.Pins;
using SlimDX.Direct3D11;
using VVVV.DX11.Internals.Effects;
using VVVV.DX11;
#endregion usings

namespace RecLafti
{
    public class DX11ShaderGraphVarManager : DX11ShaderVariableManager, IDX11ShaderVariableManager
    {
        Func<ShaderTraverseResult> traverseResult;

        public DX11ShaderGraphVarManager(IPluginHost host, IIOFactory iofactory, Func<ShaderTraverseResult> traverseResult)
            : base(host, iofactory)
        {
            this.traverseResult = traverseResult;
        }

        protected override void CreatePin(EffectVariable var)
        {
            if (var.AsInterface() != null)
            {
                if (var.LinkClasses().Length == 0)
                {
                    if (var.GetVariableType().Description.Elements == 0)
                    {
                        /*InterfaceShaderPin ip = new InterfaceShaderPin(var, this.host, this.iofactory);
                        ip.ParentEffect = this.shader.DefaultEffect;
                        this.shaderpins.Add(var.Description.Name, ip);*/
                    }

                }
                else
                {
                    RestrictedInterfaceShaderPin rp = new RestrictedInterfaceShaderPin();
                    rp.Initialize(this.iofactory, var);
                    rp.ParentEffect = this.shader.DefaultEffect;
                    this.shaderpins.Add(var.Description.Name, rp);
                }
                return;
            }

            //Search for render variable first
            if (ShaderPinFactory.IsRenderVariable(var))
            {
                IRenderVariable rv = ShaderPinFactory.GetRenderVariable(var, this.host, this.iofactory);
                this.rendervariables.Add(rv.Name, rv);
            }
            else if (ShaderPinFactory.IsWorldRenderVariable(var))
            {
                IWorldRenderVariable wv = ShaderPinFactory.GetWorldRenderVariable(var, this.host, this.iofactory);
                this.worldvariables.Add(wv.Name, wv);
            }
            else if (ShaderPinFactory.IsShaderPin(var))
            {
                IShaderPin sp = traverseResult().Pins[var.Description.Name];
                sp.Initialize(iofactory, var);
                if (sp != null) { this.shaderpins.Add(sp.Name, sp); }
            }
            else
            {
                if (var.Description.Semantic != "IMMUTABLE" && var.Description.Semantic != "")
                {
                    this.customvariables.Add(new DX11CustomRenderVariable(var));
                }
            }
        }
    }

    #region PluginInfo
    [PluginInfo(Name = "PixelShader", Category = ShaderGraph.Category)]
	#endregion PluginInfo
	public class ShaderGraphPixelShader : DX11ShaderNode, IPartImportsSatisfiedNotification, IPluginEvaluate
    {
        [Input("Shader")]
        public Pin<IFunctionNode<Vector4>> FShaderProvider; 
        Field<IFunctionNode<Vector4>> ShaderField = new Field<IFunctionNode<Vector4>>();        

        [Input("Apply Shader")]
        public ISpread<bool> FApply;

        [Output("Shader Code")]
        public ISpread<string> FCode;

        [ImportingConstructor()]
        public ShaderGraphPixelShader(IPluginHost host, IIOFactory factory)
        : base(host, factory)
        { }

        ShaderTraverseResult traverseResult;

        protected override IDX11ShaderVariableManager GetVarManager(IPluginHost host, IIOFactory iofactory)
            => new DX11ShaderGraphVarManager(host, iofactory, () => this.traverseResult);        

        void WriteEffectValue(string name, int slice)
        {
            
        }

        public new void Evaluate(int SpreadMax)
        {
            base.Evaluate(SpreadMax);
            if (FApply[0] || ShaderField.Changed(FShaderProvider))
                RecalcShader(false);
        }

        void IPartImportsSatisfiedNotification.OnImportsSatisfied()
        {
            base.OnImportsSatisfied();
            FShaderProvider.Connected += FShaderProvider_Connected;
            RecalcShader(true);
        }

        private void FShaderProvider_Connected(object sender, PinConnectionEventArgs args)
        {
            RecalcShader(false);
        }

        public void RecalcShader(bool isNew)
        { 
            var declarations = "";
            var pscode = @"
    float4 col = float4(1, 1, 1, 1);
    return col;";

            if (FShaderProvider.SliceCount > 0 && FShaderProvider[0] != null)
            {
                this.traverseResult = FShaderProvider[0].Traverse();

                declarations = string.Join(@"
", traverseResult.GlobalVars.Select(gv => gv.Value.ToString()).Concat(traverseResult.FunctionDeclarations.Values));

                pscode = string.Join(@"
    ", traverseResult.LocalDeclarations.Values) + $@"
    return {traverseResult.LocalDeclarations[FShaderProvider[0]].Identifier};";
;
            }

            var effectText = declarations +
@" 
SamplerState linearSampler: IMMUTABLE
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = Clamp;
    AddressV = Clamp;
};

cbuffer cbPerDraw : register(b0)
{
    float4x4 tVP : LAYERVIEWPROJECTION;
};

cbuffer cbPerObj : register(b1)
{
    float4x4 tW : WORLD;
};

struct VS_IN
{
    float4 PosO : POSITION;
	float4 TexCd : TEXCOORD0;
};

struct vs2ps
{
    float4 PosWVP: SV_Position;
    float2 TexCd: TEXCOORD0;
};

vs2ps VS(VS_IN input)
{
    vs2ps output;
    output.PosWVP = mul(input.PosO, mul(tW, tVP));
    output.TexCd = input.TexCd.xy;
    return output;
}

float4 PS(vs2ps In): SV_Target
{
    "
    + pscode +
@"
}

technique10 Patched
{
    pass P0
    {
        SetVertexShader(CompileShader(vs_4_0, VS()));
        SetPixelShader(CompileShader(ps_4_0, PS()));
    }
}";

            FCode.SliceCount = 1;
            FCode[0] = effectText;

            SetShader(DX11Effect.FromString(effectText), isNew, "");
        }

	}
}
