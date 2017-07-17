using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FeralTic.DX11;
using SlimDX;
using SlimDX.Direct3D11;
using VVVV.DX11.Internals.Effects.Pins;
using VVVV.DX11.Nodes.Layers;
using VVVV.PluginInterfaces.V2;

namespace RecLafti
{
    public interface IShaderNode
    {
        bool HasChanged { get; }
    }

    public interface IShaderNode<T> : IShaderNode
    {
    }

    public interface IMadeUpShaderNode : IShaderNode
    {
        string Phrase { get; }
    }

    public class MadeUpShaderNode : IMadeUpShaderNode
    {
        public string Phrase { get; private set; }
        public bool HasChanged => false;

        public MadeUpShaderNode(string phrase)
        {
            Phrase = phrase;
        }
    }

    public interface IFunctionNode : IShaderNode
    {
        string Name { get; }
        string Code { get; }
        VarDeclaration CreateShaderCallAssignment(string localVar, IEnumerable<string> arguments);
        IEnumerable<IShaderNode> NodeArguments { get; }
    }

    public interface IFunctionNode<T> : IFunctionNode, IShaderNode<T>
    {
    }

    public interface IGlobalVarNode : IShaderNode
    {
        VarDeclaration GetGlobalVar(string[] reserved);
        void SetEffectValue(EffectVariable sv, int i);
    }

    public interface IGlobalVarNode<T> : IGlobalVarNode, IShaderNode<T>
    {
    }

    public struct VarDeclaration
    {
        public VarDeclaration(string type, string identifier, string rightHandSide, string annotations = null)
        {
            Type = type;
            Identifier = identifier;
            RightHandSide = rightHandSide;
            Annotations = annotations;
        }

        public string Type;
        public string Identifier;
        public string RightHandSide;
        public string Annotations;

        public override string ToString() =>
            Annotations != null ?
                $"{Type} {Identifier} <{Annotations}> = {RightHandSide};" :
                $"{Type} {Identifier} = {RightHandSide};";
    }

    public class Default<T> : IGlobalVarNode<T>
    {
        string Name;
        T Value;

        public Default(string name)
        {
            Name = name;
            Value = default(T);
        }
        public Default(string name, T value)
        {
            Name = name;
            Value = value;
        }

        public bool HasChanged => false;

        public VarDeclaration GetGlobalVar(string[] reserved)
            => new VarDeclaration(ShaderGraph.GetTypeName<T>(), ShaderGraph.GetUniqueName(Name, reserved), Value.GetHLSLValue());

        public void SetEffectValue(EffectVariable sv, int i) => EffectVariableHelpers.Set(sv, (dynamic)Value);
    }

    public class ShaderTraverseResult
    {
        public Dictionary<string, string> FunctionDeclarations = new Dictionary<string, string>();
        public Dictionary<IGlobalVarNode, VarDeclaration> GlobalVars = new Dictionary<IGlobalVarNode, VarDeclaration>();
        public Dictionary<IFunctionNode, VarDeclaration> LocalDeclarations = new Dictionary<IFunctionNode, VarDeclaration>();
        public Dictionary<string, IShaderPin> Pins = new Dictionary<string, IShaderPin>(); 

        public string GetPhrase(IFunctionNode node) => LocalDeclarations[node].Identifier;
        public string GetPhrase(IMadeUpShaderNode node) => node.Phrase;
        public string GetPhrase(IGlobalVarNode node) => GlobalVars[node].Identifier;
        public string GetPhrase(IShaderNode node) => GetPhrase((dynamic)node);
    }

    public class ShaderGraphPin : IShaderPin
    {
        IGlobalVarNode node;

        public ShaderGraphPin(IGlobalVarNode node)
        {
            this.node = node;
        }

        public string Name { get; private set; }
        public int Elements { get; private set; }
        public string TypeName { get; private set; }
        public bool Constant => true;
        public int SliceCount => 1;

        string IShaderPin.PinName => Name;

        public void Initialize(IIOFactory factory, EffectVariable variable)
        {
            this.TypeName = variable.GetVariableType().Description.TypeName;
            this.Elements = variable.GetVariableType().Description.Elements;
            this.Name = variable.Description.Name;
        }

        public Action<int> CreateAction(DX11ShaderInstance instance)
        {
            var sv = instance.Effect.GetVariableByName(this.Name);
            return i => node.SetEffectValue(sv, i);
        }

        public void Update(EffectVariable variable)
        {
        }

        public void Dispose()
        {
        }
    }

    public static class ShaderGraph
    {
        public const string Category = "DX11.ShaderGraph";
        public const string DistanceField2DVersion = "DistanceField2D";

        public static ShaderTraverseResult Traverse(this IShaderNode node, ShaderTraverseResult result = null)
        {
            if (result == null)
                result = new ShaderTraverseResult();

            if (node != null)
                return Traverse((dynamic)node, result);

            return result;
        }

        public static ShaderTraverseResult Traverse(this IMadeUpShaderNode node, ShaderTraverseResult result = null) => result;

        static ShaderTraverseResult Traverse(this IGlobalVarNode node, ShaderTraverseResult result = null)
        {
            if (result.GlobalVars.ContainsKey(node))
                return result;

            var globalVar = node.GetGlobalVar(result.GlobalVars.Values.Select(gv => gv.Identifier).ToArray());
            result.GlobalVars[node] = globalVar;
            result.Pins[globalVar.Identifier] = new ShaderGraphPin(node);
            return result;
        }

        static ShaderTraverseResult Traverse(this IFunctionNode node, ShaderTraverseResult result = null)
        {
            if (result.LocalDeclarations.ContainsKey(node))
                return result;

            foreach (var arg in node.NodeArguments)
                arg.Traverse(result);

            result.FunctionDeclarations[node.Name] = node.Code;

            var args = node.NodeArguments.Select(a => result.GetPhrase(a));

            result.LocalDeclarations[node] = node.CreateShaderCallAssignment($"var{result.LocalDeclarations.Count}", args);

            return result;
        }

        public static string GetUniqueName(this string preferredName, string[] reserved)
        {
            if (string.IsNullOrWhiteSpace(preferredName))
                preferredName = "Anonymous";

            if (reserved.Contains(preferredName))
            {
                int i = 2;
                while (reserved.Contains(preferredName + i)) i++;
                return preferredName + i;
            }
            return preferredName;
        }

        public static string GetTypeName<T>()
        {
            if (typeof(T) == typeof(float)) return "float";
            if (typeof(T) == typeof(Vector2)) return "float2";
            if (typeof(T) == typeof(Vector3)) return "float3";
            if (typeof(T) == typeof(Vector4)) return "float4";

            throw new NotImplementedException();
        }

        static string GetHLSLValue(float value) => value.ToString(CultureInfo.InvariantCulture.NumberFormat);
        static string GetHLSLValue(Vector2 value) => $"float2({GetHLSLValue(value.X)}, {GetHLSLValue(value.Y)})";
        static string GetHLSLValue(Vector3 value) => $"float3({GetHLSLValue(value.X)}, {GetHLSLValue(value.Y)}, {GetHLSLValue(value.Z)})";
        static string GetHLSLValue(Vector4 value) => $"float4({GetHLSLValue(value.X)}, {GetHLSLValue(value.Y)}, {GetHLSLValue(value.Z)}, {GetHLSLValue(value.W)})";
        static string GetHLSLValue(Color4 value) => $"float4({GetHLSLValue(value.Red)}, {GetHLSLValue(value.Green)}, {GetHLSLValue(value.Blue)}, {GetHLSLValue(value.Alpha)})";
        public static string GetHLSLValue<T>(this T value) => GetHLSLValue((dynamic)value);



    }
}