using System.Collections.Generic;
using System.Linq;

using D_Parser.Dom;
using D_Parser.Resolver;
using D_Parser.Resolver.ASTScanner;
using D_Parser.Resolver.TypeResolution;

namespace MonoDevelop.Debugger.Gdb.D
{
	class MemberLookup : AbstractVisitor
	{
		List<KeyValuePair<TemplateIntermediateType, MemberSymbol[]>> res = new List<KeyValuePair<TemplateIntermediateType, MemberSymbol[]>>();
		List<MemberSymbol> tempMembers = new List<MemberSymbol>();

		MemberLookup(ResolutionContext ctxt)
			: base(ctxt)
		{
		}

		public static List<DSymbol> ListMembers(TemplateIntermediateType tiType, ResolutionContext ctx)
		{
			var lMembers = MemberLookup.GetMembers(tiType, ctx);
			var members = new List<DSymbol>();
			if (lMembers != null && lMembers.Length > 0) {
				foreach (var kvp in lMembers) {
					if (kvp.Value != null && kvp.Value.Length > 0) {
						foreach (var ms in kvp.Value) {
							members.Add(ms);
						}
					}
					if (kvp.Key.BaseInterfaces != null && kvp.Key.BaseInterfaces.Length > 0)
						foreach (var itf in kvp.Key.BaseInterfaces) {
							members.Add(itf);
						}
				}
			}
			return members;
		}

		protected static KeyValuePair<TemplateIntermediateType, MemberSymbol[]>[] GetMembers(TemplateIntermediateType ct, ResolutionContext ctxt)
		{
			var lk = new MemberLookup(ctxt);

			bool isBase = false;

			while (ct != null)
			{
				lk.scanChildren(ct.Definition, MemberFilter.Variables, false, isBase, false, false);

				lk.res.Add(new KeyValuePair<TemplateIntermediateType, MemberSymbol[]>(ct, lk.tempMembers.ToArray()));
				lk.tempMembers.Clear();

				ct = ct.Base as TemplateIntermediateType;
				isBase = true;
			}

			lk.res.Reverse();
			return lk.res.ToArray();
		}

		protected override bool HandleItem(PackageSymbol pack)
		{
			return false;
		}

		protected override bool HandleItem(INode n)
		{
			var dv = n as DVariable;
			if (dv != null && !dv.IsAlias && !dv.IsStatic)
			{
				//TODO: Mixins & template mixins - their mixed-in var definitions are handled _after_ the actual definition.
				tempMembers.Add(TypeDeclarationResolver.HandleNodeMatch(dv, ctxt) as MemberSymbol);
			}
			return false;
		}

		public override IEnumerable<DModule> PrefilterSubnodes(ModulePackage pack, out ModulePackage[] subPackages)
		{
			subPackages = null;
			return null;
			//return base.PrefilterSubnodes(pack, out subPackages);
		}

		public override IEnumerable<INode> PrefilterSubnodes(IBlockNode bn)
		{
			var vars = new List<INode>();
			foreach (var n in bn)
				if (n is DVariable && !(n as DVariable).IsAlias)
					vars.Add(n);
			if (vars.Count == 0)
				return null;
			return vars;
		}
	}
}
