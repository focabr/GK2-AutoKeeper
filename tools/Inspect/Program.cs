using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// usage: inspect <dll> <type regex> [member regex]
if (args[0] == "--refs") { Refs.Dump(args[1], args[2]); return; }
var dll = args[0]; var tre = new System.Text.RegularExpressions.Regex(args[1]);
bool il = args.Contains("--il"); args = args.Where(a => a != "--il").ToArray();
var mre = args.Length > 2 ? new System.Text.RegularExpressions.Regex(args[2], System.Text.RegularExpressions.RegexOptions.IgnoreCase) : null;
using var fs = File.OpenRead(dll); using var pe = new PEReader(fs); var md = pe.GetMetadataReader();
var prov = new Prov(md);
string TName(TypeDefinition t) { var n = md.GetString(t.Name); var ns = md.GetString(t.Namespace); if (t.GetDeclaringType() is var d && !d.IsNil) return TName(md.GetTypeDefinition(d)) + "+" + n; return string.IsNullOrEmpty(ns) ? n : ns + "." + n; }
foreach (var th in md.TypeDefinitions) {
  var t = md.GetTypeDefinition(th); var name = TName(t);
  if (!tre.IsMatch(name)) continue;
  string bt = t.BaseType.IsNil ? "" : " : " + prov.Handle(t.BaseType);
  Console.WriteLine($"== {name}{bt}");
  foreach (var fh in t.GetFields()) { var f = md.GetFieldDefinition(fh); var n = md.GetString(f.Name); if (mre != null && !mre.IsMatch(n)) continue;
    Console.WriteLine($"  F {((f.Attributes & FieldAttributes.Static)!=0?"static ":"")}{f.DecodeSignature(prov, null)} {n}"); }
  foreach (var ph in t.GetProperties()) { var p = md.GetPropertyDefinition(ph); var n = md.GetString(p.Name); if (mre != null && !mre.IsMatch(n)) continue;
    var sig = p.DecodeSignature(prov, null); Console.WriteLine($"  P {sig.ReturnType} {n}"); }
  foreach (var mh in t.GetMethods()) { var m = md.GetMethodDefinition(mh); var n = md.GetString(m.Name); if (mre != null && !mre.IsMatch(n)) continue;
    if (n.StartsWith("get_")||n.StartsWith("set_")) { if (mre==null) continue; }
    var sig = m.DecodeSignature(prov, null);
    var pn = m.GetParameters().Select(h => md.GetParameter(h)).Where(p=>p.SequenceNumber>0).OrderBy(p=>p.SequenceNumber).Select(p=>md.GetString(p.Name)).ToList();
    var ps = sig.ParameterTypes.Select((pt,i)=> pt + " " + (i<pn.Count?pn[i]:"")).ToList();
    Console.WriteLine($"  M {((m.Attributes & MethodAttributes.Static)!=0?"static ":"")}{((m.Attributes&MethodAttributes.MemberAccessMask)==MethodAttributes.Public?"public ":"")}{sig.ReturnType} {n}({string.Join(", ", ps)})");
    if (il && m.RelativeVirtualAddress != 0) Il.Dump(md, pe.GetMethodBody(m.RelativeVirtualAddress), prov); }
}
class Prov : ISignatureTypeProvider<string, object> {
  MetadataReader md; public Prov(MetadataReader m) { md = m; }
  public string Handle(EntityHandle h) => h.Kind switch { HandleKind.TypeDefinition => GetTypeFromDefinition(md,(TypeDefinitionHandle)h,0), HandleKind.TypeReference => GetTypeFromReference(md,(TypeReferenceHandle)h,0), HandleKind.TypeSpecification => "spec", _ => "?" };
  public string GetArrayType(string e, ArrayShape s) => e + "[,]"; public string GetByReferenceType(string e) => "ref " + e;
  public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr"; public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
  public string GetGenericMethodParameter(object c, int i) => "!!" + i; public string GetGenericTypeParameter(object c, int i) => "!" + i;
  public string GetModifiedType(string m, string u, bool r) => u; public string GetPinnedType(string e) => e; public string GetPointerType(string e) => e + "*";
  public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString().ToLower(); public string GetSZArrayType(string e) => e + "[]";
  public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => r.GetString(r.GetTypeDefinition(h).Name);
  public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => r.GetString(r.GetTypeReference(h).Name);
  public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => "spec";
}

static class Il {
  static readonly Dictionary<int, System.Reflection.Emit.OpCode> ops = typeof(System.Reflection.Emit.OpCodes).GetFields().Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)).ToDictionary(o => (int)(ushort)o.Value);
  static string Tok(MetadataReader md, int tok, Prov prov) {
    var h = MetadataTokens.EntityHandle(tok);
    try {
    switch (h.Kind) {
      case HandleKind.MethodDefinition: { var m = md.GetMethodDefinition((MethodDefinitionHandle)h); return prov.Handle(m.GetDeclaringType()) + "::" + md.GetString(m.Name); }
      case HandleKind.MemberReference: { var r = md.GetMemberReference((MemberReferenceHandle)h); return prov.Handle(r.Parent) + "::" + md.GetString(r.Name); }
      case HandleKind.FieldDefinition: { var f = md.GetFieldDefinition((FieldDefinitionHandle)h); return prov.Handle(f.GetDeclaringType()) + "::" + md.GetString(f.Name); }
      case HandleKind.MethodSpecification: { var ms = md.GetMethodSpecification((MethodSpecificationHandle)h); return Tok(md, MetadataTokens.GetToken(ms.Method), prov) + "<>"; }
      case HandleKind.TypeDefinition: case HandleKind.TypeReference: return prov.Handle(h);
      case HandleKind.TypeSpecification: return "typespec";
    } } catch {}
    return h.Kind.ToString();
  }
  public static void Dump(MetadataReader md, MethodBodyBlock body, Prov prov) {
    var r = body.GetILReader();
    while (r.RemainingBytes > 0) {
      int off = r.Offset; int b = r.ReadByte(); int code = b == 0xFE ? 0xFE00 | r.ReadByte() : b;
      var op = ops[code]; string arg = "";
      switch (op.OperandType) {
        case System.Reflection.Emit.OperandType.InlineNone: break;
        case System.Reflection.Emit.OperandType.ShortInlineBrTarget: { int d = r.ReadSByte(); arg = "IL_" + (r.Offset + d).ToString("x4"); break; }
        case System.Reflection.Emit.OperandType.InlineBrTarget: { int d = r.ReadInt32(); arg = "IL_" + (r.Offset + d).ToString("x4"); break; }
        case System.Reflection.Emit.OperandType.ShortInlineI: case System.Reflection.Emit.OperandType.ShortInlineVar: arg = r.ReadByte().ToString(); break;
        case System.Reflection.Emit.OperandType.InlineVar: arg = r.ReadUInt16().ToString(); break;
        case System.Reflection.Emit.OperandType.InlineI: arg = r.ReadInt32().ToString(); break;
        case System.Reflection.Emit.OperandType.InlineI8: arg = r.ReadInt64().ToString(); break;
        case System.Reflection.Emit.OperandType.ShortInlineR: arg = r.ReadSingle().ToString(System.Globalization.CultureInfo.InvariantCulture); break;
        case System.Reflection.Emit.OperandType.InlineR: arg = r.ReadDouble().ToString(System.Globalization.CultureInfo.InvariantCulture); break;
        case System.Reflection.Emit.OperandType.InlineString: arg = "\"" + md.GetUserString(MetadataTokens.UserStringHandle(r.ReadInt32() & 0xFFFFFF)) + "\""; break;
        case System.Reflection.Emit.OperandType.InlineSwitch: { int n = r.ReadInt32(); var t = new List<int>(); for (int i = 0; i < n; i++) t.Add(r.ReadInt32()); int bse = r.Offset; arg = string.Join(",", t.Select(x => "IL_" + (bse + x).ToString("x4"))); break; }
        default: arg = Tok(md, r.ReadInt32(), prov); break;
      }
      Console.WriteLine($"      IL_{off:x4} {op.Name} {arg}");
    }
  }
}
