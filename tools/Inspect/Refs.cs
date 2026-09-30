using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
static class Refs {
  public static void Dump(string dll, string asmFilter) {
    using var fs = File.OpenRead(dll); using var pe = new PEReader(fs); var md = pe.GetMetadataReader(); var prov = new Prov(md);
    foreach (var ar in md.AssemblyReferences) { var a = md.GetAssemblyReference(ar); Console.WriteLine($"asmref {md.GetString(a.Name)} {a.Version}"); }
    string Scope(TypeReferenceHandle h) { var t = md.GetTypeReference(h); if (t.ResolutionScope.Kind == HandleKind.AssemblyReference) return md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)t.ResolutionScope).Name); if (t.ResolutionScope.Kind == HandleKind.TypeReference) return Scope((TypeReferenceHandle)t.ResolutionScope); return "?"; }
    foreach (var th in md.TypeReferences) { var t = md.GetTypeReference(th); if (Scope(th) == asmFilter) Console.WriteLine($"typeref {md.GetString(t.Namespace)}.{md.GetString(t.Name)}"); }
    foreach (var mh in md.MemberReferences) {
      var m = md.GetMemberReference(mh); string parent; string asm = "?";
      if (m.Parent.Kind == HandleKind.TypeReference) { parent = prov.Handle(m.Parent); asm = Scope((TypeReferenceHandle)m.Parent); }
      else if (m.Parent.Kind == HandleKind.TypeSpecification) { var ts = md.GetTypeSpecification((TypeSpecificationHandle)m.Parent); parent = ts.DecodeSignature(prov, null); asm = "spec"; }
      else parent = m.Parent.Kind.ToString();
      if (asm != asmFilter && !(asm=="spec" && parent.Contains(asmFilter))) { if (asm != "spec") continue; }
      string sig;
      if (m.GetKind() == MemberReferenceKind.Method) { var s = m.DecodeMethodSignature(prov, null); sig = $"{(s.Header.IsInstance?"":"static ")}{s.ReturnType} ({string.Join(", ", s.ParameterTypes)})" + (s.GenericParameterCount>0?$" gen{s.GenericParameterCount}":""); }
      else sig = "field " + m.DecodeFieldSignature(prov, null);
      Console.WriteLine($"memberref [{asm}] {parent}::{md.GetString(m.Name)} {sig}");
    }
  }
}
