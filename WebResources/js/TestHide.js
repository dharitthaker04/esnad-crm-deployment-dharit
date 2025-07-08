function hideReturnToCustomerStageOnLoad() {
    const targetStageName = "return to customer"; // lowercase for matching
    let attempts = 0;
    const maxAttempts = 5;

    const tryHide = () => {
        const stageElements = document.querySelectorAll("li[data-id][title]");
        if (!stageElements.length) {
            console.log("⏳ BPF stages not yet loaded.");
            return false;
        }

        let hidden = false;
        stageElements.forEach((el) => {
            const title = el.getAttribute("title")?.trim().toLowerCase();
            if (title === targetStageName) {
                el.style.display = "none";
                console.log(`🚫 Hidden BPF stage: ${title}`);
                hidden = true;
            }
        });

        return hidden;
    };

    const interval = setInterval(() => {
        const success = tryHide();
        attempts++;
        if (success || attempts >= maxAttempts) {
            clearInterval(interval);
        }
    }, 1000);
}
