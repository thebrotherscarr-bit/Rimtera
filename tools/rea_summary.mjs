// Helper for tools/rea_storyteller.ps1. Reads REA inspect-managed-members JSON output and keeps names only.
//
//   node tools/rea_summary.mjs check <file.json>          prints "ok", or REA's limitation text and exits 1
//   node tools/rea_summary.mjs summarize <rawDir> <outDir>  writes summary.json and index.json for every *.json in rawDir
//
// The summary holds each type's field names and types, method names with parameter and return types, and which
// methods each method calls (by name). No method bodies, constants or values.
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join, basename } from "node:path";

function load(file) {
    return JSON.parse(readFileSync(file, "utf8")).normalized_result;
}

function readable(result) {
    return result?.coverage?.state === "complete" && (result.fields?.length ?? 0) + (result.methods?.length ?? 0) > 0;
}

function summarize(type, result) {
    const shortCaller = (caller) => caller.startsWith(type + ".") ? caller.slice(type.length + 1) : caller;
    return {
        type,
        fields: (result.fields ?? []).map((field) => `${field.name}: ${field.signature?.field_type ?? "?"}`),
        methods: (result.methods ?? []).map((method) =>
            `${method.name}(${(method.signature?.parameter_types ?? []).join(", ")}) -> ${method.signature?.return_type ?? "?"}`),
        calls: [...new Set((result.call_edges ?? []).map((edge) =>
            `${shortCaller(edge.caller)} -> ${edge.target_declaring_type}.${edge.target_name}`))].sort(),
    };
}

const [mode, first, second] = process.argv.slice(2);
if (mode === "check") {
    let result;
    try { result = load(first); } catch (error) { console.log(`could not read the output: ${error.message}`); process.exit(1); }
    if (readable(result)) { console.log("ok"); process.exit(0); }
    console.log([...(result?.limitations ?? []), ...(result?.coverage?.issues ?? []).map((issue) => issue.detail)].join(" ") || "no members found");
    process.exit(1);
} else if (mode === "summarize") {
    const summary = [];
    const index = [];
    for (const name of readdirSync(first).filter((file) => file.endsWith(".json")).sort()) {
        const type = basename(name, ".json");
        let result = null;
        try { result = load(join(first, name)); } catch { }
        const found = readable(result);
        index.push({ type, found });
        if (found) summary.push(summarize(type, result));
    }
    writeFileSync(join(second, "summary.json"), JSON.stringify(summary, null, 1));
    writeFileSync(join(second, "index.json"), JSON.stringify(index, null, 1));
    console.log(`${index.filter((entry) => entry.found).length} of ${index.length}`);
} else {
    console.log("usage: check <file.json> | summarize <rawDir> <outDir>");
    process.exit(2);
}
