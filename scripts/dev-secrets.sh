#!/usr/bin/env sh
# Copies secrets from .env into .NET user-secrets shared by the Api, Worker and Migrations projects.
# Development only: secrets stay in the user profile, outside the repository.
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
env_file=${1:-"$script_dir/../.env"}
project="$script_dir/../Backend/BookingPlatform.Api/BookingPlatform.Api.csproj"

if [ ! -f "$env_file" ]; then
  echo "No .env file at '$env_file'. Copy .env.example to .env and set local passwords first." >&2
  exit 1
fi

set_secret() {
  key=$1
  required=$2
  value=$(grep -E "^${key}=" "$env_file" | head -n 1 | cut -d '=' -f 2- || true)
  if [ -z "$value" ] || printf '%s' "$value" | grep -q 'CHANGE_ME'; then
    if [ "$required" = "yes" ]; then
      echo "$key is missing or still contains CHANGE_ME in $env_file." >&2
      exit 1
    fi
    return 0
  fi
  secret_name=$(printf '%s' "$key" | sed 's/__/:/g')
  dotnet user-secrets set "$secret_name" "$value" --project "$project" > /dev/null
  echo "Set user secret $secret_name"
}

for key in ConnectionStrings__Postgres ConnectionStrings__Redis Jwt__SigningKey PublicBooking__TokenKey DEV_ADMIN_PASSWORD; do
  set_secret "$key" yes
done
set_secret DEV_ADMIN_EMAIL no

echo "Done. Api, Worker and Migrations share these secrets (same UserSecretsId)."
