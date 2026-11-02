"""Reconstruct and render exact mutation syntax with the pinned SDK Roslyn parser."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess
MODE='roslyn-expression-v1'
_BUILT={}
def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def render_mutation(root,source_text,location,replacement):
 root=Path(root).resolve(); helper=Path(__file__).with_name('mutation-renderer.cs')
 version=json.loads((root/'global.json').read_text(encoding='utf-8-sig'))['sdk']['version']
 dotnet=Path(os.environ['DOTNET_ROOT'])/('dotnet.exe' if os.name=='nt' else 'dotnet') if os.environ.get('DOTNET_ROOT') else Path(shutil.which('dotnet') or '')
 if not dotnet.is_file(): raise ValueError('Pinned .NET executable unavailable.')
 if subprocess.check_output([str(dotnet),'--version'],cwd=root,text=True).strip()!=version: raise ValueError('Renderer SDK differs from global.json.')
 key=hashlib.sha256((digest(helper)+version).encode()).hexdigest(); output=root/'development/mutation-renderer'/key/str(os.getpid()); output.mkdir(parents=True,exist_ok=True)
 project=output/'renderer.csproj'; source=output/'Program.cs'; source.write_bytes(helper.read_bytes())
 project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Reference Include="Microsoft.CodeAnalysis"><HintPath>$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.dll</HintPath></Reference><Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll</HintPath></Reference></ItemGroup></Project>',encoding='utf-8')
 assembly=output/'bin/Release/net10.0/renderer.dll'
 if str(output) not in _BUILT:
  with (output/'build.log').open('w',encoding='utf-8') as log: subprocess.run([str(dotnet),'build',str(project),'-c','Release','--nologo','-t:Rebuild'],cwd=root,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=120)
  _BUILT[str(output)]={str(f):digest(f) for f in [assembly,assembly.with_name('Microsoft.CodeAnalysis.CSharp.dll'),assembly.with_name('Microsoft.CodeAnalysis.dll')]}
 if any(digest(f)!=sha for f,sha in _BUILT[str(output)].items()): raise ValueError('Renderer compiled artifacts changed.')
 request={'source':source_text,'location':location,'replacement':replacement}
 run=subprocess.run([str(dotnet),str(assembly)],cwd=root,input=json.dumps(request),text=True,capture_output=True,timeout=60)
 if run.returncode: raise ValueError('Mutation rendering failed: '+run.stderr.strip())
 value=json.loads(run.stdout)
 receipt={'replacementMode':MODE,'rendererSourceSha256':digest(helper),'rendererModuleSha256':digest(__file__),'sdkVersion':version,'roslynSha256':digest(assembly.with_name('Microsoft.CodeAnalysis.CSharp.dll')),'roslynCoreSha256':digest(assembly.with_name('Microsoft.CodeAnalysis.dll')),'sourceSha256':hashlib.sha256(source_text.encode('utf-8')).hexdigest(),'location':location,'replacement':replacement,**{k:value[k] for k in ['nodeKind','renderedReplacement','expectedShapeSha256','renderedShapeSha256']}}
 return value['rendered'],value['originalSpan'],receipt
