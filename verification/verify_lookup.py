import http.server
import os
import socketserver
import threading

from playwright.sync_api import sync_playwright

PORT = 9099


class MyHTTPRequestHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        super().end_headers()


def run_server():
    os.chdir("/app")
    handler = MyHTTPRequestHandler
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("", PORT), handler) as httpd:
        print(f"Serving HTTP at port {PORT}")
        httpd.serve_forever()


def run_verification():
    server_thread = threading.Thread(target=run_server, daemon=True)
    server_thread.start()

    import time

    time.sleep(1)

    url = f"http://localhost:{PORT}/verification/index.html"
    screenshot_path = "/app/verification/verification.png"

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1000, "height": 1100})

        print(f"Opening local HTTP verification page: {url}")
        page.goto(url)

        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(1500)

        print(f"Taking screenshot at: {screenshot_path}")
        page.screenshot(path=screenshot_path)

        browser.close()
        print("Verification screenshot captured successfully.")


if __name__ == "__main__":
    run_verification()
