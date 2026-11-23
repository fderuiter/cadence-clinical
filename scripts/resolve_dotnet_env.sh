#!/bin/sh
# ==============================================================================
# Cadence Clinical Platform - Shared POSIX .NET SDK Environment Resolver
# Ensures ~/.dotnet/tools stays on PATH and promotes a verified local .NET 10
# SDK ($HOME/.dotnet or %LOCALAPPDATA%\Microsoft\dotnet) only when active dotnet
# on PATH fails to satisfy global.json and dotnet + host/fxr + sdk/10.* exist.
# ==============================================================================

_cadence_prepend_path() {
    _cadence_target_dir="$1"
    [ -n "$_cadence_target_dir" ] || return 0
    _cadence_new_path="$_cadence_target_dir"
    _cadence_old_ifs="${IFS:-}"
    _cadence_had_ifs=0
    if [ "${IFS+x}" = "x" ]; then
        _cadence_had_ifs=1
    fi
    IFS=":"
    for _cadence_entry in ${PATH:-}; do
        if [ -n "$_cadence_entry" ] && [ "$_cadence_entry" != "$_cadence_target_dir" ]; then
            _cadence_new_path="${_cadence_new_path}:${_cadence_entry}"
        fi
    done
    if [ "$_cadence_had_ifs" -eq 1 ]; then
        IFS="$_cadence_old_ifs"
    else
        unset IFS
    fi
    PATH="$_cadence_new_path"
    export PATH
}

_cadence_append_missing_path() {
    _cadence_tools_dir="$1"
    [ -n "$_cadence_tools_dir" ] || return 0
    case ":${PATH:-}:" in
        *":$_cadence_tools_dir:"*) ;;
        *)
            PATH="${_cadence_tools_dir}${PATH:+:$PATH}"
            export PATH
            ;;
    esac
}

_cadence_sdk_version_satisfies() {
    _cadence_ver_candidate="$1"
    [ -n "$_cadence_ver_candidate" ] || return 1

    if [ "$_cadence_allow_prerelease" != "true" ]; then
        case "$_cadence_ver_candidate" in
            *-*) return 1 ;;
        esac
    fi

    _cadence_ver_core="${_cadence_ver_candidate%%-*}"
    case "$_cadence_ver_core" in
        [0-9]*.[0-9]*.[0-9]*) ;;
        *) return 1 ;;
    esac

    _cadence_cand_major="${_cadence_ver_core%%.*}"
    _cadence_cand_rest="${_cadence_ver_core#*.}"
    _cadence_cand_minor="${_cadence_cand_rest%%.*}"
    _cadence_cand_rest="${_cadence_cand_rest#*.}"
    _cadence_cand_patch="${_cadence_cand_rest%%.*}"

    case "${_cadence_cand_major}${_cadence_cand_minor}${_cadence_cand_patch}" in
        *[!0-9]*|"") return 1 ;;
    esac

    case "$_cadence_roll_forward" in
        latestMajor|major)
            if [ "$_cadence_cand_major" -gt "$_cadence_req_major" ]; then
                return 0
            fi
            if [ "$_cadence_cand_major" -lt "$_cadence_req_major" ]; then
                return 1
            fi
            if [ "$_cadence_cand_minor" -gt "$_cadence_req_minor" ]; then
                return 0
            fi
            [ "$_cadence_cand_minor" -eq "$_cadence_req_minor" ] && [ "$_cadence_cand_patch" -ge "$_cadence_req_patch" ]
            return $?
            ;;
        latestMinor|minor)
            [ "$_cadence_cand_major" -eq "$_cadence_req_major" ] || return 1
            if [ "$_cadence_cand_minor" -gt "$_cadence_req_minor" ]; then
                return 0
            fi
            [ "$_cadence_cand_minor" -eq "$_cadence_req_minor" ] && [ "$_cadence_cand_patch" -ge "$_cadence_req_patch" ]
            return $?
            ;;
        latestPatch)
            [ "$_cadence_cand_major" -eq "$_cadence_req_major" ] || return 1
            [ "$_cadence_cand_minor" -eq "$_cadence_req_minor" ] || return 1
            _cadence_req_band=$(( _cadence_req_patch / 100 ))
            _cadence_cand_band=$(( _cadence_cand_patch / 100 ))
            [ "$_cadence_cand_band" -eq "$_cadence_req_band" ] && [ "$_cadence_cand_patch" -ge "$_cadence_req_patch" ]
            return $?
            ;;
        disable)
            [ "$_cadence_cand_major" -eq "$_cadence_req_major" ] &&
            [ "$_cadence_cand_minor" -eq "$_cadence_req_minor" ] &&
            [ "$_cadence_cand_patch" -eq "$_cadence_req_patch" ]
            return $?
            ;;
        *)
            # latestFeature / feature (global.json default in Cadence)
            [ "$_cadence_cand_major" -eq "$_cadence_req_major" ] &&
            [ "$_cadence_cand_minor" -eq "$_cadence_req_minor" ] &&
            [ "$_cadence_cand_patch" -ge "$_cadence_req_patch" ]
            return $?
            ;;
    esac
}

_cadence_is_valid_dotnet_sdk_dir() {
    _cadence_sdk_root="$1"
    [ -n "$_cadence_sdk_root" ] || return 1
    if [ ! -x "$_cadence_sdk_root/dotnet" ] && [ ! -f "$_cadence_sdk_root/dotnet.exe" ]; then
        return 1
    fi
    [ -d "$_cadence_sdk_root/host/fxr" ] || return 1
    [ -d "$_cadence_sdk_root/sdk" ] || return 1

    for _cadence_sdk_entry in "$_cadence_sdk_root/sdk/${_cadence_req_major}."*; do
        [ -d "$_cadence_sdk_entry" ] || continue
        _cadence_sdk_folder="${_cadence_sdk_entry##*/}"
        if _cadence_sdk_version_satisfies "$_cadence_sdk_folder"; then
            return 0
        fi
    done

    return 1
}

_cadence_active_dotnet_satisfies_global_json() {
    command -v dotnet >/dev/null 2>&1 || return 1

    if _cadence_active_ver="$(
        if [ -n "$_cadence_resolve_root" ] && [ -d "$_cadence_resolve_root" ]; then
            cd "$_cadence_resolve_root" || exit 1
        fi
        DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_NOLOGO=1 dotnet --version 2>/dev/null
    )"; then
        _cadence_cr="$(printf '\r')"
        _cadence_active_ver="${_cadence_active_ver%"$_cadence_cr"}"
        _cadence_sdk_version_satisfies "$_cadence_active_ver"
        return $?
    fi

    return 1
}

_cadence_resolve_dotnet_env() {
    _cadence_resolve_root="${CADENCE_RESOLVE_REPO_ROOT:-${REPO_ROOT:-}}"
    if [ -z "$_cadence_resolve_root" ] && [ -f "global.json" ]; then
        _cadence_resolve_root="$(pwd)"
    fi

    _cadence_req_major="10"
    _cadence_req_minor="0"
    _cadence_req_patch="100"
    _cadence_roll_forward="latestFeature"
    _cadence_allow_prerelease="false"

    if [ -n "$_cadence_resolve_root" ] && [ -f "$_cadence_resolve_root/global.json" ]; then
        _cadence_cr="$(printf '\r')"
        while IFS= read -r _cadence_json_line || [ -n "$_cadence_json_line" ]; do
            _cadence_json_line="${_cadence_json_line%"$_cadence_cr"}"
            case "$_cadence_json_line" in
                *'"version"'*)
                    _cadence_parsed="${_cadence_json_line#*\"version\"}"
                    _cadence_parsed="${_cadence_parsed#*\"}"
                    _cadence_parsed="${_cadence_parsed%%\"*}"
                    case "$_cadence_parsed" in
                        [0-9]*.[0-9]*.[0-9]*)
                            _cadence_req_major="${_cadence_parsed%%.*}"
                            _cadence_rest="${_cadence_parsed#*.}"
                            _cadence_req_minor="${_cadence_rest%%.*}"
                            _cadence_rest="${_cadence_rest#*.}"
                            _cadence_req_patch="${_cadence_rest%%[-.]*}"
                            ;;
                    esac
                    ;;
                *'"rollForward"'*)
                    _cadence_rf="${_cadence_json_line#*\"rollForward\"}"
                    _cadence_rf="${_cadence_rf#*\"}"
                    _cadence_rf="${_cadence_rf%%\"*}"
                    if [ -n "$_cadence_rf" ]; then
                        _cadence_roll_forward="$_cadence_rf"
                    fi
                    ;;
                *'"allowPrerelease"'*true*)
                    _cadence_allow_prerelease="true"
                    ;;
                *'"allowPrerelease"'*false*)
                    _cadence_allow_prerelease="false"
                    ;;
            esac
        done < "$_cadence_resolve_root/global.json"
    fi

    _cadence_home="${HOME:-${USERPROFILE:-}}"
    if [ -n "$_cadence_home" ]; then
        _cadence_home_posix="$_cadence_home"
        case "$_cadence_home_posix" in
            ?:\\*|?:/*)
                if command -v cygpath >/dev/null 2>&1; then
                    _cadence_home_posix="$(cygpath -u "$_cadence_home_posix")"
                fi
                ;;
        esac
        if [ -d "$_cadence_home_posix/.dotnet/tools" ]; then
            _cadence_append_missing_path "$_cadence_home_posix/.dotnet/tools"
        fi
    fi

    if _cadence_active_dotnet_satisfies_global_json; then
        return 0
    fi

    if [ -n "${DOTNET_ROOT:-}" ]; then
        _cadence_existing_root="$DOTNET_ROOT"
        _cadence_existing_posix="$_cadence_existing_root"
        case "$_cadence_existing_posix" in
            ?:\\*|?:/*)
                if command -v cygpath >/dev/null 2>&1; then
                    _cadence_existing_posix="$(cygpath -u "$_cadence_existing_posix")"
                fi
                ;;
        esac
        if _cadence_is_valid_dotnet_sdk_dir "$_cadence_existing_posix"; then
            DOTNET_ROOT="$_cadence_existing_root"
            export DOTNET_ROOT
            _cadence_prepend_path "$_cadence_existing_posix"
            return 0
        fi
    fi

    if [ -n "$_cadence_home" ]; then
        _cadence_home_dotnet_posix="$_cadence_home_posix/.dotnet"
        case "$_cadence_home" in
            ?:\\*|?:/*) _cadence_home_dotnet_root="${_cadence_home}\\.dotnet" ;;
            *) _cadence_home_dotnet_root="$_cadence_home_dotnet_posix" ;;
        esac
        if _cadence_is_valid_dotnet_sdk_dir "$_cadence_home_dotnet_posix"; then
            DOTNET_ROOT="$_cadence_home_dotnet_root"
            export DOTNET_ROOT
            _cadence_prepend_path "$_cadence_home_dotnet_posix"
            return 0
        fi
    fi

    if [ -n "${LOCALAPPDATA:-}" ]; then
        _cadence_localappdata_posix="$LOCALAPPDATA"
        case "$LOCALAPPDATA" in
            ?:\\*|?:/*)
                if command -v cygpath >/dev/null 2>&1; then
                    _cadence_localappdata_posix="$(cygpath -u "$LOCALAPPDATA")"
                fi
                _cadence_localappdata_dotnet_root="${LOCALAPPDATA}\\Microsoft\\dotnet"
                ;;
            *)
                _cadence_localappdata_dotnet_root="${LOCALAPPDATA}/Microsoft/dotnet"
                ;;
        esac
        _cadence_localappdata_dotnet_posix="${_cadence_localappdata_posix}/Microsoft/dotnet"
        if _cadence_is_valid_dotnet_sdk_dir "$_cadence_localappdata_dotnet_posix"; then
            DOTNET_ROOT="$_cadence_localappdata_dotnet_root"
            export DOTNET_ROOT
            _cadence_prepend_path "$_cadence_localappdata_dotnet_posix"
            return 0
        fi
    fi

    return 0
}

_cadence_resolve_dotnet_env
