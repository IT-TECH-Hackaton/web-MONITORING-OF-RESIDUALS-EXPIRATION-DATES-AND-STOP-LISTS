import { execSync } from "node:child_process";

const url = process.env.OPENAPI_URL ?? "http://127.0.0.1:5080/swagger/v1/swagger.json";
execSync(`npx openapi-typescript "${url}" -o src/generated.ts`, { stdio: "inherit" });
console.log("OpenAPI types written to src/generated.ts");
