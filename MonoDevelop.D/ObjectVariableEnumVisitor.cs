using D_Parser.Dom;
using D_Parser.Resolver;
using D_Parser.Resolver.ASTScanner;
using D_Parser.Resolver.TypeResolution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoDevelop.Debugger.Gdb.D
{
	class ObjectMemberOffsetLookup : AbstractVisitor
	{
		List<KeyValuePair<ClassType, MemberSymbol[]>> res = new List<KeyValuePair<ClassType, MemberSymbol[]>>();
		List<MemberSymbol> tempMembers = new List<MemberSymbol>();

		ObjectMemberOffsetLookup(ResolutionContext ctxt)
			: base(ctxt)
		{

		}

		public static KeyValuePair<ClassType, MemberSymbol[]>[] GetMembers(ClassType ct, ResolutionContext ctxt)
		{
			var lk = new ObjectMemberOffsetLookup(ctxt);

			bool isBase = false;
			bool bk = false;

			while (ct != null)
			{
				lk.scanChildren(ct.Definition, MemberFilter.Variables, ref bk, false, isBase, false, false);

				lk.res.Add(new KeyValuePair<ClassType, MemberSymbol[]>(ct, lk.tempMembers.ToArray()));
				lk.tempMembers.Clear();

				ct = ct.Base as ClassType;
				isBase = true;
			}

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

		public override IEnumerable<IAbstractSyntaxTree> PrefilterSubnodes(ModulePackage pack, out ModulePackage[] subPackages)
		{
			subPackages = null;
			return null;
			//return base.PrefilterSubnodes(pack, out subPackages);
		}

		public override System.Collections.Generic.IEnumerable<INode> PrefilterSubnodes(IBlockNode bn)
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
