// Issue #98: `@excalidraw/mermaid-to-excalidraw` yerine kurulan yerel paket (package.json resolutions/overrides).
// Gerçek paket mermaid + d3 getirir; d3 modüllerini ngx-charts ile paylaştığı için ilk yükleme bundle'ını ~40 KB
// büyütüyordu. İmza gerçek paketle aynı: `parseMermaidToExcalidraw(definition, config?)` → Promise. Reddedilen promise'i
// Excalidraw iki yerde de yakalar: "Mermaid'den diyagram" iletişim kutusu hata mesajı gösterir, Mermaid'e benzeyen
// yapıştırılan metin düz metin olarak eklenir.
export const parseMermaidToExcalidraw = async (_definition, _config) => {
  throw new Error('Mermaid diagrams are disabled on this whiteboard.');
};
