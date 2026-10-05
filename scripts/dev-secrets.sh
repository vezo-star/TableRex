#!/usr/bin/env sh
# Copies ConnectionStrings__* values from .env into .NET user-secrets shared by the Api, Worker and Migrations projects.
# Development only: secrets stay in the user profile, outside the repository.
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
env_file=${1:-"$script_dir/../.env"}
project="$script_dir/../Backend/BookingPlatform.Api/BookingPlatform.Api.csproj"

if [ ! -f "$env_file" ]; then
  echo "No .env file at '$env_file'. Copy .env.example to .env and set local passwords first." >&2
  exit 1
fi

for key in ConnectionStrings__Postgres ConnectionStrings__Redis; do
  value=$(grep -E "^${key}=" "$env_file" | head -n 1 | cut -d '=' -f 2-)
  if [ -z "$value" ] || printf '%s' "$value" | grep -q 'CHANGE_ME'; then
    echo "$key is missing or still contains CHANGE_ME in $env_file." >&2
    exit 1
  fi
  secret_name=$(printf '%s' "$key" | sed 's/__/:/g')
  dotnet user-secrets set "$secret_name" "$value" --project "$project" > /dev/null
  echo "Set user secret $secret_name"
done

echo "Done. Api, Worker and Migrations share these secrets (same UserSecretsId)."
