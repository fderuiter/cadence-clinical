#!/usr/bin/env python3
import os
import socket

import yaml

DEFAULT_PORTS = {
    "Gateway API": 8000,
    "Designer Service": 8001,
    "Execution Service": 8002,
    "eTMF Service": 8003,
    "CTMS Service": 8007,
    "Interop Service": 8004,
    "Notifications Service": 8006,
    "Quality Service": 8005,
    "Safety Service": 8008,
    "Tickets Service": 8009,
    "eConsent Service": 8012,
    "eISF Service": 8010,
}


def load_ports_from_compose():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    docker_compose_path = os.path.join(root_dir, "docker", "docker-compose.yml")

    if not os.path.exists(docker_compose_path):
        return

    try:
        with open(docker_compose_path) as f:
            compose_data = yaml.safe_load(f)
        services = compose_data.get("services", {})

        # Mapping from docker-compose service name to display name
        compose_mapping = {
            "gateway": "Gateway API",
            "designer": "Designer Service",
            "execution": "Execution Service",
            "etmf": "eTMF Service",
            "ctms": "CTMS Service",
            "interop": "Interop Service",
            "notifications": "Notifications Service",
            "quality": "Quality Service",
            "safety": "Safety Service",
            "tickets": "Tickets Service",
            "eisf": "eISF Service",
        }

        for compose_name, display_name in compose_mapping.items():
            if compose_name in services:
                ports_list = services[compose_name].get("ports", [])
                if ports_list and isinstance(ports_list, list):
                    port_entry = ports_list[0]
                    if isinstance(port_entry, str):
                        if ":" in port_entry:
                            host_port = int(port_entry.split(":")[0])
                        else:
                            host_port = int(port_entry)
                    elif isinstance(port_entry, (int, float)):
                        host_port = int(port_entry)
                    else:
                        continue
                    DEFAULT_PORTS[display_name] = host_port

        # Explicitly map/fallback eConsent Service to 8012 and eISF Service to 8010.
        # Check if "org" exists in services to extract the dynamic host port mapping for Org/eConsent.
        if "org" in services:
            ports_list = services["org"].get("ports", [])
            if ports_list and isinstance(ports_list, list):
                port_entry = ports_list[0]
                if isinstance(port_entry, str):
                    if ":" in port_entry:
                        ports_val = int(port_entry.split(":")[0])
                    else:
                        ports_val = int(port_entry)
                elif isinstance(port_entry, (int, float)):
                    ports_val = int(port_entry)
                else:
                    ports_val = 8012
                DEFAULT_PORTS["eConsent Service"] = ports_val
            else:
                DEFAULT_PORTS["eConsent Service"] = 8012
        else:
            DEFAULT_PORTS["eConsent Service"] = 8012

    except Exception:
        # Fall back to aligned defaults
        pass

    # Ensure final safety overrides
    if DEFAULT_PORTS.get("eConsent Service") is None:
        DEFAULT_PORTS["eConsent Service"] = 8012
    if DEFAULT_PORTS.get("eISF Service") is None:
        DEFAULT_PORTS["eISF Service"] = 8010


# Load dynamic values to override DEFAULT_PORTS in place
load_ports_from_compose()


def is_port_in_use(port: int, host: str = "127.0.0.1") -> bool:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.5)
        return s.connect_ex((host, port)) == 0


def main():
    print("--- Cadence Clinical Microservice Port Allocation Check ---")
    collisions = []
    available = []

    for service, port in DEFAULT_PORTS.items():
        if is_port_in_use(port):
            collisions.append((service, port))
            print(f"[!] PORT IN USE: {service:<24} on port {port}")
        else:
            available.append((service, port))
            print(f"[✓] AVAILABLE:   {service:<24} on port {port}")

    print("\n--- Summary ---")
    if collisions:
        print(
            f"Warning: {len(collisions)} port(s) currently bound by active processes."
        )
        print(
            "Running tests or dev servers on these ports will cause 'address already in use' errors."
        )
    else:
        print("All microservice ports are free and ready for local development!")


if __name__ == "__main__":
    main()
