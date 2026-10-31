# Drittanbieter, Herkunft und Lizenzprüfung

## Eigener Quellbaum

Die vorhandene MIT-Lizenz mit dem ursprünglichen Copyright-Hinweis „Arena.ai Desktop Companion contributors“ bleibt unverändert enthalten. ISAS ist die gewünschte Produktmarke, keine Behauptung alleiniger historischer Urheberschaft und keine bestätigte Arena-Partnerschaft.

## Direkte Entwicklungskomponenten

| Komponente | Verwendung | Lizenz-/Verteilungszuständigkeit |
|---|---|---|
| .NET 8 / Windows Desktop Runtime | WPF und Laufzeit | Microsoft/.NET-Hinweise der konkret publizierten Runtime beachten. |
| Microsoft.Web.WebView2 1.0.4129.50 | SDK und Browserintegration | Microsoft-Paketbedingungen; Browserruntime hat eigene Bedingungen. |
| Microsoft.NET.Test.Sdk | Testhost | Bedingungen des tatsächlich restaurierten Pakets beachten. |
| xUnit / Visual Studio Runner | Regressionen | Paketlizenz und beigefügte Hinweise beachten. |
| WiX Toolset 4.0.6 | MSI-Bauwerkzeug | WiX-Lizenzbedingungen prüfen; Buildwerkzeug ist nicht die gesamte Produktlizenz. |
| Node.js | Eingebetteten Client ausführen / Tests | Node-Lizenzhinweise der verwendeten Laufzeit beachten; Node wird nicht im MSI mitgebündelt. |

Keine pauschale Behauptung, alle transitiven Pakete besäßen dieselbe Lizenz. Vor tatsächlicher Weiterverteilung Self-contained-Payload, Runtimehinweise und transitive Paketliste prüfen. Die gepflegten Projektdateien nennen direkte Versionen; Restorelogs und erzeugte Assets liefern die konkrete transitive Auflösung.

## Namen, Bilder und Services

Arena, Microsoft, Windows und Visual Studio bezeichnen jeweilige Anbieter/Produkte, keine bestätigte Zertifizierung. Das vorhandene Anwendungssymbol wurde aus den übernommenen Projektassets verwendet. Der echte Statusausschnitt stammt aus bereitgestelltem Anwendungsmaterial und wurde auf den relevanten Bereich reduziert; persönliche Pfade sind verdeckt. SVG-Grafiken wurden für diese Projektdokumentation erstellt.

Externe Dienste sind Laufzeitabhängigkeiten und nicht unter der Projekt-MIT-Lizenz mitlizenziert. Aktuelle Nutzungsbedingungen und Verfügbarkeit sind vor produktiver Nutzung gesondert zu beachten.

## Ergänzte Werkzeuge

Offizielle GitHub CLI 2.103.3 (MIT) wird mit SHA256-geprüftem Archiv als Produktwerkzeug mitgeliefert; LICENSE liegt bei der CLI. Git ist eine separate Voraussetzung für Clone/Upload. Gitleaks 8.30.1 (MIT) wird ebenfalls nach SHA256-Prüfung mit LICENSE mitgeliefert: Build-/Historienprüfung und unverzichtbarer Snapshot-Secret-Gate vor GitHub-Uploads. WiX 4.0.6/BAL (MS-RL, gepinnte freie Toolchain) erstellt MSI/Burn-Bootstrapper; keine neuere lizenzpflichtige Toolchain wird still verwendet.
