#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
output_dir="${repo_root}/publish/Tiro.Cli"

rm -rf "${output_dir}"
dotnet publish "${repo_root}/src/Tiro.Cli/Tiro.Cli.csproj" -c Release -o "${output_dir}"

echo "Published Tiro v3 CLI to ${output_dir}"
