#!/bin/bash
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    CREATE DATABASE fincore_identity;
    CREATE DATABASE fincore_accounts;
    GRANT ALL PRIVILEGES ON DATABASE fincore_identity TO $POSTGRES_USER;
    GRANT ALL PRIVILEGES ON DATABASE fincore_accounts TO $POSTGRES_USER;
EOSQL

echo "Databases fincore_identity and fincore_accounts created."
