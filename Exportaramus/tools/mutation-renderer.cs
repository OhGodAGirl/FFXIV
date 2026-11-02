using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static string Shape(SyntaxNode node)
{
    if (node is ParenthesizedExpressionSyntax expression) return Shape(expression.Expression);
    if (node is ParenthesizedPatternSyntax pattern) return Shape(pattern.Pattern);
    return "N" + node.RawKind + "[" + string.Join(",", node.ChildNodesAndTokens().Select(child => child.IsNode ? Shape(child.AsNode()!) : "T" + child.RawKind + ":" + child.AsToken().Text)) + "]";
}
var request = JsonDocument.Parse(Console.In.ReadToEnd()).RootElement;
var source = request.GetProperty("source").GetString()!;
var replacement = request.GetProperty("replacement").GetString()!;
var location = request.GetProperty("location");
var text = SourceText.From(source);
int Offset(JsonElement position)
{
    int line = position.GetProperty("line").GetInt32() - 1;
    int column = position.GetProperty("column").GetInt32() - 1;
    if (line < 0 || line >= text.Lines.Count || column < 0 || column > text.Lines[line].Span.Length) throw new InvalidDataException("Invalid UTF-16 mutation location.");
    return text.Lines[line].Start + column;
}
var span = TextSpan.FromBounds(Offset(location.GetProperty("start")), Offset(location.GetProperty("end")));
if (span.IsEmpty) throw new InvalidDataException("Empty mutation location.");
var options = new CSharpParseOptions(LanguageVersion.Preview);
var root = CSharpSyntaxTree.ParseText(text, options).GetRoot();
var node = root.FindNode(span, getInnermostNodeForTie: true);
if (node.Span != span) throw new InvalidDataException("Mutation location is not an exact syntax node.");
SyntaxNode parsed;
if (node is PatternSyntax)
{
    var pattern = SyntaxFactory.ParseExpression("x is " + replacement, options: options) as IsPatternExpressionSyntax;
    parsed = pattern?.Pattern ?? throw new InvalidDataException("Replacement is not a pattern.");
}
else if (node is ExpressionSyntax)
{
    parsed = SyntaxFactory.ParseExpression(replacement, options: options);
    if (node is MemberBindingExpressionSyntax && parsed.ContainsDiagnostics)
    {
        var contextual = SyntaxFactory.ParseExpression("x?" + replacement, options: options) as ConditionalAccessExpressionSyntax;
        parsed = contextual?.WhenNotNull ?? throw new InvalidDataException("Invalid conditional member binding.");
    }
}
else if (node is StatementSyntax) parsed = SyntaxFactory.ParseStatement(replacement, options: options);
else throw new InvalidDataException("Unsupported mutation node: " + node.Kind());
// Stryker binary mutations retain the original children, rather than reparsing display text.
var oldTokens = node.DescendantTokens().ToArray();
var newTokens = parsed.DescendantTokens().ToArray();
var changed = oldTokens.Length == newTokens.Length ? Enumerable.Range(0, oldTokens.Length).Where(i => oldTokens[i].RawKind != newTokens[i].RawKind || oldTokens[i].Text != newTokens[i].Text).ToArray() : [];
if (changed.Length == 1 && node is BinaryExpressionSyntax binary && oldTokens[changed[0]] == binary.OperatorToken)
{
    var kind = SyntaxFacts.GetBinaryExpression(newTokens[changed[0]].Kind());
    if (kind == SyntaxKind.None) throw new InvalidDataException("Unknown binary operator replacement.");
    parsed = SyntaxFactory.BinaryExpression(kind, binary.Left, binary.Right);
}
if (changed.Length == 1 && node is BinaryPatternSyntax binaryPattern && oldTokens[changed[0]] == binaryPattern.OperatorToken)
{
    var token = newTokens[changed[0]];
    var kind = token.IsKind(SyntaxKind.AndKeyword) ? SyntaxKind.AndPattern : token.IsKind(SyntaxKind.OrKeyword) ? SyntaxKind.OrPattern : SyntaxKind.None;
    if (kind == SyntaxKind.None) throw new InvalidDataException("Unknown pattern operator replacement.");
    parsed = SyntaxFactory.BinaryPattern(kind, binaryPattern.Left, token, binaryPattern.Right);
}
if (parsed.ContainsDiagnostics) throw new InvalidDataException("Replacement syntax has parse diagnostics.");
var expected = root.ReplaceNode(node, parsed.WithTriviaFrom(node));
var expectedShape = Shape(expected);
string renderedReplacement = replacement;
string Render(string value) => source[..span.Start] + value + source[span.End..];
var rendered = Render(renderedReplacement);
var actual = CSharpSyntaxTree.ParseText(rendered, options).GetRoot();
if (Shape(actual) != expectedShape)
{
    if (node is ExpressionSyntax && node is not MemberBindingExpressionSyntax || node is PatternSyntax)
    {
        renderedReplacement = new ProtectPrecedence().Visit(parsed)!.ToFullString();
        if (node is ExpressionSyntax) renderedReplacement = "(" + renderedReplacement + ")";
        else renderedReplacement = "(" + renderedReplacement + ")";
        rendered = Render(renderedReplacement);
        actual = CSharpSyntaxTree.ParseText(rendered, options).GetRoot();
    }
}
if (Shape(actual) != expectedShape) throw new InvalidDataException("Rendered mutation differs from the intended syntax tree.");
Console.Write(JsonSerializer.Serialize(new { rendered, originalSpan = source[span.Start..span.End], nodeKind = node.Kind().ToString(), renderedReplacement, expectedShapeSha256 = Hash(expectedShape), renderedShapeSha256 = Hash(Shape(actual)) }));

sealed class ProtectPrecedence : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node) => SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)base.VisitBinaryExpression(node)!);
    public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node) => SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)base.VisitConditionalExpression(node)!);
    public override SyntaxNode? VisitBinaryPattern(BinaryPatternSyntax node) => SyntaxFactory.ParenthesizedPattern((PatternSyntax)base.VisitBinaryPattern(node)!);
}
