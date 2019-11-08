#!/bin/sh -eu
{ set +x; } 2>/dev/null
SOURCE="${BASH_SOURCE[0]}"
DIR="$( cd -P "$( dirname "$SOURCE" )" >/dev/null 2>&1 && pwd )"

OS="Mac"
if [[ -e "/c/" ]]; then
  OS="Windows"
fi

CONFIGURATION=""
PUBLIC=""

while (( "$#" )); do
  case "$1" in
    -d|--debug)
      CONFIGURATION="Debug"
      shift
    ;;
    -r|--release)
      CONFIGURATION="Release"
      shift
    ;;
    -p|--public)
      PUBLIC="/p:PublicRelease=true"
      shift
    ;;
    -*|--*=) # unsupported flags
      echo "Error: Unsupported flag $1" >&2
      exit 1
      ;;
    *) # preserve positional arguments
      if [[ x"$CONFIGURATION" != x"" ]]; then
        echo "Invalid argument $1"
        exit -1
      fi
      CONFIGURATION="$1"
      shift
      ;;
  esac
done

if [[ x"$CONFIGURATION" == x"" ]]; then
  CONFIGURATION="Debug"
fi

if [[ x"$OS" == x"Windows" && x"$PUBLIC" != x"" ]]; then
  PUBLIC="/$PUBLIC"
fi

pushd $DIR >/dev/null 2>&1
dotnet restore
dotnet build --no-restore -c $CONFIGURATION $PUBLIC
popd >/dev/null 2>&1