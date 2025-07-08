function loadBootstrapDependencies() {
    // Load Bootstrap only once
    if (!window.bootstrap) {
        const link = document.createElement("link");
        link.href = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";
        link.rel = "stylesheet";
        document.head.appendChild(link);

        const script = document.createElement("script");
        script.src = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js";
        document.body.appendChild(script);
    }
}

function openReopenModal() {
    loadBootstrapDependencies();

    setTimeout(() => {
        if (!document.getElementById("ticketModal")) {
            const modalHtml = `
            <div class="modal fade" id="ticketModal" tabindex="-1" aria-labelledby="ticketModalLabel" aria-hidden="true">
              <div class="modal-dialog">
                <div class="modal-content p-3">
                  <div class="modal-header">
                    <h5 class="modal-title">Ticket Reopen</h5>
                   
                  </div>
                  <div class="modal-body">
                    <label for="title">Title</label>
                    <input type="text" id="title" class="form-control" placeholder="Enter title" />
                    <label for="comment" class="mt-2">Comment</label>
                    <textarea id="comment" class="form-control" rows="4" placeholder="Enter comment"></textarea>
                  </div>
                  <div class="modal-footer justify-content-between">
                    <button type="button" class="btn btn-primary" onclick="submitReopenForm()">Submit</button>
         
                  </div>
                </div>
              </div>
            </div>`;
            const div = document.createElement('div');
            div.innerHTML = modalHtml;
            document.body.appendChild(div);
        }

        const myModal = new bootstrap.Modal(document.getElementById('ticketModal'));
        myModal.show();
    }, 500); // Delay to ensure Bootstrap loads
}

function submitReopenForm() {
    const title = document.getElementById("title").value;
    const comment = document.getElementById("comment").value;

    console.log("Submitted Title:", title);
    console.log("Submitted Comment:", comment);

    const modalInstance = bootstrap.Modal.getInstance(document.getElementById("ticketModal"));
    modalInstance.hide();
}
