# lib/

- `Mono.Cecil.dll` —— Mono.Cecil 0.11.5，MIT 许可，版权归 Jb Evain。
  取自 NuGet 包 `mono.cecil` 0.11.5 的 `lib/net40/Mono.Cecil.dll`，未做修改。

构建时通过 `/resource:lib\Mono.Cecil.dll,Mono.Cecil.dll` 内嵌进最终 exe，
运行期由程序内的 `AssemblyResolve` 从嵌入资源加载，因此发布物是单文件。
