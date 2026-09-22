using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the two-by-two matrix support the shader runtime gained: the <c>float2x2</c> constructor
/// from scalars and from a vector, and HLSL's <c>mul</c> in both argument orders and for two
/// matrices. A scan of the preset collection found 729 files that build a <c>float2x2</c> and consume
/// it with <c>mul</c>, so these values are what restores those presets' shaders.
/// </summary>
public sealed class ShaderMatrixTests
{
    /// <summary>A matrix times a column vector uses the row-major layout.</summary>
    [Fact]
    public void Mul_MatrixTimesVector()
    {
        // [[0, -1], [1, 0]] * (1, 0) = (0, 1)
        var result = Run("return float4(mul(float2x2(0, -1, 1, 0), float2(1, 0)), 0, 1);");

        Assert.Equal(0f, result.X, 4);
        Assert.Equal(1f, result.Y, 4);
    }

    /// <summary>A row vector times a matrix is the other argument order.</summary>
    [Fact]
    public void Mul_VectorTimesMatrix()
    {
        // (1, 0) * [[0, -1], [1, 0]] = (0, -1)
        var result = Run("return float4(mul(float2(1, 0), float2x2(0, -1, 1, 0)), 0, 1);");

        Assert.Equal(0f, result.X, 4);
        Assert.Equal(-1f, result.Y, 4);
    }

    /// <summary>Two matrices multiply, and the product can be applied to a vector.</summary>
    [Fact]
    public void Mul_MatrixTimesMatrix()
    {
        // [[1, 2], [3, 4]] * [[5, 6], [7, 8]] = [[19, 22], [43, 50]]; times (1, 0) = (19, 43)
        var result = Run(
            "return float4(mul(mul(float2x2(1, 2, 3, 4), float2x2(5, 6, 7, 8)), float2(1, 0)), 0, 1);");

        Assert.Equal(19f, result.X, 4);
        Assert.Equal(43f, result.Y, 4);
    }

    /// <summary>A matrix built from a float4 fills row-major, the way the presets spell it.</summary>
    [Fact]
    public void Construct_FromVectorFillsRowMajor()
    {
        // _qb = (1, 2, 3, 4) -> [[1, 2], [3, 4]]; times (1, 0) = (1, 3)
        var result = Run(
            "return float4(mul(float2x2(_qb), float2(1, 0)), 0, 1);",
            ("_qb", ShaderValue.Vector(1f, 2f, 3f, 4f, 4)));

        Assert.Equal(1f, result.X, 4);
        Assert.Equal(3f, result.Y, 4);
    }

    /// <summary>A matrix declared and read back keeps its row-major layout.</summary>
    [Fact]
    public void Declaration_KeepsTheMatrix()
    {
        var result = Run("""
            float2x2 rot = float2x2(0, -1, 1, 0);
            return float4(mul(rot, float2(0, 1)), 0, 1);
            """);

        Assert.Equal(-1f, result.X, 4);
        Assert.Equal(0f, result.Y, 4);
    }

    /// <summary>A three-by-three matrix times a column vector uses the row-major layout.</summary>
    [Fact]
    public void Mul_ThreeByThreeMatrixTimesVector()
    {
        // [[1,2,3],[4,5,6],[7,8,9]] * (1,0,0) = (1,4,7)
        var result = Run("return float4(mul(float3x3(1, 2, 3, 4, 5, 6, 7, 8, 9), float3(1, 0, 0)), 1);");

        Assert.Equal(1f, result.X, 4);
        Assert.Equal(4f, result.Y, 4);
        Assert.Equal(7f, result.Z, 4);
    }

    /// <summary>A row vector times a three-by-three matrix is the other argument order.</summary>
    [Fact]
    public void Mul_VectorTimesThreeByThreeMatrix()
    {
        // (1,0,0) * [[1,2,3],[4,5,6],[7,8,9]] = (1,2,3)
        var result = Run("return float4(mul(float3(1, 0, 0), float3x3(1, 2, 3, 4, 5, 6, 7, 8, 9)), 1);");

        Assert.Equal(1f, result.X, 4);
        Assert.Equal(2f, result.Y, 4);
        Assert.Equal(3f, result.Z, 4);
    }

    /// <summary>A static const three-by-three matrix, the shape the collection uses, works.</summary>
    [Fact]
    public void Mul_StaticConstThreeByThreeMatrix()
    {
        var result = Run("""
            static const float3x3 RotMat = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
            return float4(mul(float3(1, 2, 3), RotMat), 1);
            """);

        Assert.Equal(1f, result.X, 4);
        Assert.Equal(2f, result.Y, 4);
        Assert.Equal(3f, result.Z, 4);
    }

    /// <summary>A four-by-four matrix works with a four-component vector.</summary>
    [Fact]
    public void Mul_FourByFourMatrixTimesVector()
    {
        var result = Run(
            "return mul(float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1), float4(1, 2, 3, 4));");

        Assert.Equal(1f, result.X, 4);
        Assert.Equal(2f, result.Y, 4);
        Assert.Equal(3f, result.Z, 4);
        Assert.Equal(4f, result.W, 4);
    }

    /// <summary>Runs a comp shader through the interpreter, which is the reference path.</summary>
    /// <param name="body">Shader body.</param>
    /// <param name="seed">Optional variable to seed before the run.</param>
    /// <returns>The returned value.</returns>
    private static ShaderValue Run(string body, (string Name, ShaderValue Value)? seed = null)
    {
        var program = ShaderParser.Parse("float4 main(float2 uv : TEXCOORD0) : COLOR { " + body + " }");
        var interpreter = new ShaderInterpreter(program, null);
        interpreter.SetVariable("uv", ShaderValue.Vector(0.25f, 0.75f, 0f, 0f, 2));
        if (seed is { } value)
            interpreter.SetVariable(value.Name, value.Value);
        return interpreter.Run();
    }
}
