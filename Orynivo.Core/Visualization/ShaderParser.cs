namespace Orynivo.Visualization;

/// <summary>
/// Recursive-descent parser for the HLSL <c>ps_2_0</c> subset Milkdrop shaders use. It accepts a
/// complete shader (global sampler and constant declarations plus one or more functions) and
/// bare statement bodies, so both a real preset shader and a short snippet parse. The result is
/// a <see cref="ShaderNode"/> tree the interpreter walks; anything the subset does not cover
/// reports its source position instead of being guessed.
/// </summary>
public static class ShaderParser
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "void", "bool", "int", "uint", "half", "half2", "half3", "half4",
        "float", "float1", "float2", "float3", "float4", "float2x2", "float3x3", "float4x4",
        "half1",
        "sampler", "sampler2D", "sampler3D", "texture"
    };

    /// <summary>Parses shader source.</summary>
    /// <param name="source">Shader source text.</param>
    /// <returns>The root node, whose children are the statements of the shader body.</returns>
    /// <exception cref="PresetExpressionException">The source is not valid for the subset.</exception>
    public static ShaderNode Parse(string? source)
    {
        var tokens = ShaderLexer.Tokenize(source);
        var parser = new State(tokens);
        var statements = new List<ShaderNode>();
        while (parser.Current.Kind != ShaderTokenKind.End)
        {
            var statement = parser.ParseStatement();
            if (statement is not null)
                statements.Add(statement);
        }

        return new ShaderNode(ShaderNodeKind.Program, 0, Children: statements);
    }

    /// <summary>Holds the token cursor while parsing.</summary>
    private sealed class State
    {
        private readonly IReadOnlyList<ShaderToken> _tokens;
        private int _index;

        /// <summary>Creates the cursor over a token stream.</summary>
        /// <param name="tokens">Tokens to walk.</param>
        public State(IReadOnlyList<ShaderToken> tokens) => _tokens = tokens;

        /// <summary>Gets the token under the cursor.</summary>
        public ShaderToken Current => _tokens[Math.Min(_index, _tokens.Count - 1)];

        /// <summary>Moves past the current token.</summary>
        public void Advance() => _index++;

        /// <summary>Consumes the expected punctuation or reports its position.</summary>
        /// <param name="text">Punctuation to consume.</param>
        /// <returns>The consumed token.</returns>
        public ShaderToken Expect(string text)
        {
            if (Current.Text != text)
                throw new PresetExpressionException($"Expected '{text}' but found '{Describe(Current)}'.", Current.Position);

            var token = Current;
            Advance();
            return token;
        }

        /// <summary>Consumes the expected identifier or reports its position.</summary>
        /// <returns>The consumed token.</returns>
        public ShaderToken ExpectIdentifier()
        {
            if (Current.Kind != ShaderTokenKind.Identifier)
                throw new PresetExpressionException($"Expected a name but found '{Describe(Current)}'.", Current.Position);

            var token = Current;
            Advance();
            return token;
        }

        /// <summary>Consumes the current token when it matches.</summary>
        /// <param name="text">Text to match.</param>
        /// <returns><see langword="true"/> when the token was consumed.</returns>
        public bool TryConsume(string text)
        {
            if (Current.Text != text)
                return false;

            Advance();
            return true;
        }

        /// <summary>Skips an optional annotation such as <c>: register(s0)</c> or <c>: COLOR</c>.</summary>
        public void SkipAnnotation()
        {
            if (!TryConsume(":"))
                return;

            if (Current.Kind is ShaderTokenKind.Identifier or ShaderTokenKind.Keyword)
                Advance();
            if (TryConsume("("))
            {
                while (Current.Kind != ShaderTokenKind.End && !TryConsume(")"))
                    Advance();
            }
        }

        /// <summary>Parses one statement, or skips a function signature and returns its body.</summary>
        /// <returns>The statement, or <see langword="null"/> when a signature was consumed.</returns>
        public ShaderNode? ParseStatement()
        {
            if (TryConsume(";"))
                return null;

            if (TryConsume("{"))
                return new ShaderNode(ShaderNodeKind.Block, Current.Position, Children: ParseBlock());

            if (Current.Kind == ShaderTokenKind.Keyword)
            {
                switch (Current.Text)
                {
                    case "if":
                        return ParseIf();
                    case "for":
                        return ParseFor();
                    case "return":
                        return ParseReturn();
                }
            }

            // A sampler declaration may omit its type, as in "sampler_main : register(s0);".
            if (Current.Kind == ShaderTokenKind.Identifier &&
                _tokens[Math.Min(_index + 1, _tokens.Count - 1)].Text == ":")
            {
                while (Current.Kind != ShaderTokenKind.End && !TryConsume(";"))
                    Advance();
                return null;
            }

            // Qualifiers do not change what a declaration means here, so they are skipped.
            while (Current.Kind == ShaderTokenKind.Keyword &&
                   Current.Text is "const" or "static" or "inline")
            {
                Advance();
            }

            // A type keyword starts either a declaration or a function signature.
            if (IsType(Current))
            {
                var type = Current;
                var lookahead = _tokens[Math.Min(_index + 1, _tokens.Count - 1)];
                if (lookahead.Kind == ShaderTokenKind.Identifier)
                {
                    Advance();
                    var name = ExpectIdentifier();
                    if (Current.Text == "(")
                        return ParseFunctionBody(type, name);
                    return ParseDeclaration(type, name);
                }
            }

            var expression = ParseExpression();
            Expect(";");
            return new ShaderNode(ShaderNodeKind.ExpressionStatement, expression.Position, Left: expression);
        }

        /// <summary>Parses a brace-delimited block.</summary>
        /// <returns>The statements of the block.</returns>
        public List<ShaderNode> ParseBlock()
        {
            var statements = new List<ShaderNode>();
            while (Current.Kind != ShaderTokenKind.End && Current.Text != "}")
            {
                var statement = ParseStatement();
                if (statement is not null)
                    statements.Add(statement);
            }

            Expect("}");
            return statements;
        }

        /// <summary>Parses a declaration, continuing after the type and name were consumed.</summary>
        /// <param name="type">Type token.</param>
        /// <param name="name">Declared name.</param>
        /// <returns>The declaration node.</returns>
        private ShaderNode ParseDeclaration(ShaderToken type, ShaderToken name)
        {
            ShaderNode? initializer = null;
            if (TryConsume("="))
                initializer = ParseExpression();
            SkipAnnotation();
            while (TryConsume(","))
            {
                ExpectIdentifier();
                if (TryConsume("="))
                    ParseExpression();
            }

            Expect(";");
            return new ShaderNode(ShaderNodeKind.Declaration, type.Position, type.Text, 0f, initializer, null, null, [new ShaderNode(ShaderNodeKind.Identifier, name.Position, name.Text)]);
        }

        /// <summary>Parses a function signature and its body.</summary>
        /// <param name="type">Return type token.</param>
        /// <param name="name">Function name token.</param>
        /// <returns>The block node holding the body.</returns>
        private ShaderNode ParseFunctionBody(ShaderToken type, ShaderToken name)
        {
            Expect("(");
            var depth = 1;
            while (Current.Kind != ShaderTokenKind.End && depth > 0)
            {
                if (Current.Text == "(")
                    depth++;
                else if (Current.Text == ")")
                    depth--;
                Advance();
            }

            SkipAnnotation();
            Expect("{");
            return new ShaderNode(ShaderNodeKind.Block, type.Position, name.Text, 0f, null, null, null, ParseBlock());
        }

        /// <summary>Parses an <c>if</c> statement with its optional <c>else</c> branch.</summary>
        /// <returns>The if node.</returns>
        private ShaderNode ParseIf()
        {
            var token = Current;
            Advance();
            Expect("(");
            var condition = ParseExpression();
            Expect(")");
            var then = ParseStatement() ?? new ShaderNode(ShaderNodeKind.Block, token.Position, Children: []);
            ShaderNode? otherwise = null;
            if (Current.Kind == ShaderTokenKind.Keyword && Current.Text == "else")
            {
                Advance();
                otherwise = ParseStatement();
            }

            return new ShaderNode(ShaderNodeKind.If, token.Position, Left: condition, Right: then, Third: otherwise);
        }

        /// <summary>Parses a <c>for</c> loop.</summary>
        /// <returns>The for node.</returns>
        private ShaderNode ParseFor()
        {
            var token = Current;
            Advance();
            Expect("(");
            ShaderNode? initializer = null;
            if (!TryConsume(";"))
            {
                initializer = IsType(Current) ? ParseStatement() : ParseExpressionStatement();
            }

            ShaderNode? condition = null;
            if (!TryConsume(";"))
            {
                condition = ParseExpression();
                Expect(";");
            }

            ShaderNode? increment = null;
            if (Current.Text != ")")
                increment = ParseExpression();
            Expect(")");
            var body = ParseStatement() ?? new ShaderNode(ShaderNodeKind.Block, token.Position, Children: []);
            return new ShaderNode(ShaderNodeKind.For, token.Position, Left: initializer, Right: condition, Third: increment, Children: [body]);
        }

        /// <summary>Parses a <c>return</c> statement.</summary>
        /// <returns>The return node.</returns>
        private ShaderNode ParseReturn()
        {
            var token = Current;
            Advance();
            ShaderNode? value = null;
            if (!TryConsume(";"))
            {
                value = ParseExpression();
                Expect(";");
            }

            return new ShaderNode(ShaderNodeKind.Return, token.Position, Left: value);
        }

        /// <summary>Parses an expression statement, including its semicolon.</summary>
        /// <returns>The statement node.</returns>
        private ShaderNode ParseExpressionStatement()
        {
            var expression = ParseExpression();
            Expect(";");
            return new ShaderNode(ShaderNodeKind.ExpressionStatement, expression.Position, Left: expression);
        }

        /// <summary>Parses an expression with the C operator precedence.</summary>
        /// <returns>The expression node.</returns>
        public ShaderNode ParseExpression() => ParseAssignment();

        private ShaderNode ParseAssignment()
        {
            var left = ParseTernary();
            string[] assignments = ["=", "+=", "-=", "*=", "/="];
            if (assignments.Contains(Current.Text))
            {
                var token = Current;
                Advance();
                var right = ParseAssignment();
                return new ShaderNode(ShaderNodeKind.Binary, token.Position, token.Text, 0f, left, right);
            }

            return left;
        }

        private ShaderNode ParseTernary()
        {
            var condition = ParseLogicalOr();
            if (!TryConsume("?"))
                return condition;

            var whenTrue = ParseExpression();
            Expect(":");
            var whenFalse = ParseTernary();
            return new ShaderNode(ShaderNodeKind.Ternary, condition.Position, "?", 0f, condition, whenTrue, whenFalse);
        }

        private ShaderNode ParseLogicalOr() => ParseBinary(ParseLogicalAnd, "||");

        private ShaderNode ParseLogicalAnd() => ParseBinary(ParseEquality, "&&");

        private ShaderNode ParseEquality() => ParseBinary(ParseRelational, "==", "!=");

        private ShaderNode ParseRelational() => ParseBinary(ParseAdditive, "<", ">", "<=", ">=");

        private ShaderNode ParseAdditive() => ParseBinary(ParseMultiplicative, "+", "-");

        private ShaderNode ParseMultiplicative() => ParseBinary(ParseUnary, "*", "/", "%");

        /// <summary>Parses a left-associative level of binary operators.</summary>
        /// <param name="next">Parser of the next tighter level.</param>
        /// <param name="operators">Operators of this level.</param>
        /// <returns>The expression node.</returns>
        private ShaderNode ParseBinary(Func<ShaderNode> next, params string[] operators)
        {
            var left = next();
            while (operators.Contains(Current.Text))
            {
                var token = Current;
                Advance();
                var right = next();
                left = new ShaderNode(ShaderNodeKind.Binary, token.Position, token.Text, 0f, left, right);
            }

            return left;
        }

        private ShaderNode ParseUnary()
        {
            string[] prefixes = ["-", "+", "!", "~", "++", "--"];
            if (prefixes.Contains(Current.Text))
            {
                var token = Current;
                Advance();
                var operand = ParseUnary();
                return new ShaderNode(ShaderNodeKind.Unary, token.Position, token.Text, 0f, operand);
            }

            return ParsePostfix();
        }

        private ShaderNode ParsePostfix()
        {
            var expression = ParsePrimary();
            while (true)
            {
                if (TryConsume("."))
                {
                    var member = ExpectIdentifier();
                    expression = new ShaderNode(ShaderNodeKind.Member, member.Position, member.Text, 0f, expression);
                    continue;
                }

                if (TryConsume("("))
                {
                    var arguments = new List<ShaderNode>();
                    if (Current.Text != ")")
                    {
                        arguments.Add(ParseExpression());
                        while (TryConsume(","))
                            arguments.Add(ParseExpression());
                    }

                    Expect(")");
                    var name = expression.Kind == ShaderNodeKind.Identifier ? expression.Text : string.Empty;
                    expression = new ShaderNode(ShaderNodeKind.Call, expression.Position, name, 0f, expression, null, null, arguments);
                    continue;
                }

                if (Current.Text is "++" or "--")
                {
                    var token = Current;
                    Advance();
                    expression = new ShaderNode(ShaderNodeKind.Unary, token.Position, token.Text, 0f, expression);
                    continue;
                }

                return expression;
            }
        }

        private ShaderNode ParsePrimary()
        {
            if (Current.Kind == ShaderTokenKind.Number)
            {
                var token = Current;
                Advance();
                return new ShaderNode(ShaderNodeKind.Literal, token.Position, token.Text, token.Number);
            }

            if (TryConsume("("))
            {
                var inner = ParseExpression();
                Expect(")");
                return inner;
            }

            if (Current.Kind is ShaderTokenKind.Identifier or ShaderTokenKind.Keyword)
            {
                // A type name in front of a constructor call, for example float2(1, 0).
                if (IsType(Current))
                {
                    var type = Current;
                    Advance();
                    if (Current.Text == "(")
                    {
                        Advance();
                        var arguments = new List<ShaderNode>();
                        if (Current.Text != ")")
                        {
                            arguments.Add(ParseExpression());
                            while (TryConsume(","))
                                arguments.Add(ParseExpression());
                        }

                        Expect(")");
                        return new ShaderNode(ShaderNodeKind.Call, type.Position, type.Text, 0f, null, null, null, arguments);
                    }

                    throw new PresetExpressionException($"Expected a constructor call after '{type.Text}'.", type.Position);
                }

                var token = Current;
                Advance();
                if (token.Text == "true" || token.Text == "false")
                    return new ShaderNode(ShaderNodeKind.Literal, token.Position, token.Text, token.Text == "true" ? 1f : 0f);
                return new ShaderNode(ShaderNodeKind.Identifier, token.Position, token.Text);
            }

            throw new PresetExpressionException($"Unexpected '{Describe(Current)}' in an expression.", Current.Position);
        }

        /// <summary>Reports whether a token names a type.</summary>
        /// <param name="token">Token to test.</param>
        /// <returns><see langword="true"/> when the token is a known type name.</returns>
        private static bool IsType(ShaderToken token) =>
            token.Kind is ShaderTokenKind.Identifier or ShaderTokenKind.Keyword && Types.Contains(token.Text);

        /// <summary>Describes a token for an error message.</summary>
        /// <param name="token">Token to describe.</param>
        /// <returns>The token text or a readable stand-in.</returns>
        private static string Describe(ShaderToken token) =>
            token.Kind == ShaderTokenKind.End ? "end of source" : token.Text;
    }
}
