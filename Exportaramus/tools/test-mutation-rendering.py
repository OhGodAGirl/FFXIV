import os
from pathlib import Path
import unittest
from mutation_rendering import render_mutation
ROOT=Path(os.environ.get('MUTATION_RENDER_TEST_ROOT',Path(__file__).resolve().parents[1]))
def render(source,span,replacement):
 start=source.index(span); before=source[:start]; line=before.count('\n')+1; column=len(before.rsplit('\n',1)[-1].encode('utf-16-le'))//2+1
 location={'start':{'line':line,'column':column},'end':{'line':line,'column':column+len(span.encode('utf-16-le'))//2}}
 return render_mutation(ROOT,source,location,replacement)
class RenderingTests(unittest.TestCase):
 def test_outer_precedence(self):
  s='class C { bool M(bool a,bool b,bool c)=>a && b && c; }'; got,old,proof=render(s,'a && b','a || b');self.assertIn('(a||b)',got.replace(' ',''));self.assertEqual('a && b',old);self.assertEqual(proof['expectedShapeSha256'],proof['renderedShapeSha256'])
 def test_internal_binary_precedence(self):
  got,_,_=render('class C { bool M(bool a,bool b,bool c)=>a || b || c; }','a || b || c','a || b && c');self.assertIn('(a || b ',got);self.assertIn('&&',got)
 def test_internal_arithmetic(self):
  got,_,_=render('class C { int M(int a,int b,int c)=>a+b+c; }','a+b+c','a+b*c');self.assertIn('(a+b)',got)
 def test_pattern_children(self):
  got,_,_=render('class C { bool M(int x)=>x is 1 or 2 or 3; }','1 or 2 or 3','1 or 2 and 3');self.assertIn('(1 or 2 ',got)
 def test_statement_removal(self):
  s='class C { void M(){return;} }';self.assertEqual(s.replace('return;',';'),render(s,'return;',';')[0])
 def test_block_removal(self):
  s='class C { void M(){M();} }';self.assertEqual('class C { void M(){} }',render(s,'{M();}','{}')[0])
 def test_member_binding(self):
  s='class C { object M(dynamic a)=>a?.Count(); }';self.assertEqual(s.replace('.Count','.Sum'),render(s,'.Count','.Sum')[0])
 def test_simple_literal(self):
  s='class C { bool M()=>true; }';self.assertEqual(s.replace('true','false'),render(s,'true','false')[0])
 def test_reject_non_node(self):
  with self.assertRaises(ValueError):render('class C { bool M()=>true; }','tru','false')
 def test_reject_invalid_replacement(self):
  with self.assertRaises(ValueError):render('class C { bool M()=>true; }','true','(')
 def test_utf16_location(self):
  s='class C { string s="😀"; bool M()=>true; }';self.assertEqual(s.replace('true','false'),render(s,'true','false')[0])
 def test_unchanged_shape_for_comparison(self):
  got,_,_=render('class C { bool M(int a,int b)=>a>=b; }','a>=b','a<b');self.assertIn('a<b',got)
if __name__=='__main__':unittest.main()
