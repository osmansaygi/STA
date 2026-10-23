from pathlib import Path
r=Path('tools/CukurSmoke');r.mkdir(exist_ok=True)
for name in ['TemelCekuru','TemelDetay']:
 s=Path('ST4_Yardimci/'+name+'Form.cs').read_text(encoding='utf-8-sig'); s=s[:s.index('    internal sealed class '+name+'Form')]
 s+='    internal static class '+name+'Form { internal static void Shutdown() {} }\n}\n'
 (r/(name+'Settings.cs')).write_text(s,encoding='utf-8-sig')
proj=Path('tools/ParapetSmoke/ParapetSmoke.csproj').read_text()
a=proj.index('    <Compile Include="Smoke.cs"'); b=proj.index('    <Reference Include="AcDbMgd"')
items='    <Compile Include="*.cs" />\n'
for f in ['TemelCekuruRunner.cs','TemelCekuruRunner.Detay.cs','TemelCekuruRunner.DetayCut.cs','TemelCekuruRunner.Plan.cs','BaDimensionStyles.cs','BaIni.cs']:
 items+='    <Compile Include="../../ST4_Yardimci/'+f+'" />\n'
proj=proj[:a]+items+proj[b:];proj=proj.replace('<EnableDefaultCompileItems>false</EnableDefaultCompileItems>','<EnableDefaultCompileItems>false</EnableDefaultCompileItems><UseWindowsForms>true</UseWindowsForms>')
(r/'CukurSmoke.csproj').write_text(proj)
