// Downloads for BrowserFileSaver.cs (backups and exports).

/** Offers the text as a file download (UTF-8; a BOM for CSV so Excel reads accents correctly). */
export function saveText(fileName, contentType, text) {
    const parts = contentType.startsWith('text/csv') ? ['﻿', text] : [text];
    const blob = new Blob(parts, { type: contentType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();

    // Give the download a moment to start before the URL goes away.
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
