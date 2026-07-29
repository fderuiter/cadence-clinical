#!/usr/bin/env python3
import socket

DEFAULT_PORTS = {
    "Gateway API": 8000,
    "Designer Service": 8001,
    "Execution Service": 8002,
    "eTMF Service": 8003,
    "CTMS Service": 8004,
    "Interop Service": 8005,
    "Notifications Service": 8006,
    "Quality Service": 8007,
    "Safety Service": 8008,
    "Tickets Service": 8009,
    "eConsent Service": 8010,
    "eISF Service": 8011,
}


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
