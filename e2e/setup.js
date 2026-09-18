import { execFileSync } from "node:child_process";

export default function setup() {
  execFileSync("dotnet", ["run", "--project", "tools/TestHostSeed/TestHostSeed.csproj"], { stdio: "inherit" });
}
