(() => {
    const root = document.getElementById("hrm-recruitment-dashboard");
    if (!root) return;

    const content = document.getElementById("hrm-dashboard-content");
    const error = document.getElementById("hrm-dashboard-error");
    const retry = document.getElementById("hrm-dashboard-retry");
    const loadTime = document.getElementById("hrm-dashboard-load-time");
    const numberFormat = new Intl.NumberFormat("vi-VN");
    const svgNamespace = "http://www.w3.org/2000/svg";

    const elements = {
        openPositions: document.getElementById("hrm-open-positions"),
        newApplications: document.getElementById("hrm-new-applications"),
        upcomingInterviews: document.getElementById("hrm-upcoming-interviews"),
        offersAwaitingResponse: document.getElementById("hrm-offers-awaiting-response"),
        applicationsChart: document.getElementById("hrm-applications-chart"),
        closedPositionsChart: document.getElementById("hrm-closed-positions-chart"),
        overduePositions: document.getElementById("hrm-overdue-positions")
    };

    function appendSvg(parent, name, attributes, text) {
        const element = document.createElementNS(svgNamespace, name);
        Object.entries(attributes).forEach(([key, value]) => element.setAttribute(key, String(value)));
        if (text !== undefined) element.textContent = text;
        parent.appendChild(element);
        return element;
    }

    function formatPeriod(value, granularity) {
        const [year, month, day] = value.split("-").map(Number);
        if (!year || !month || !day) return value;
        if (granularity === "month") return `${String(month).padStart(2, "0")}/${String(year).slice(-2)}`;
        return `${String(day).padStart(2, "0")}/${String(month).padStart(2, "0")}`;
    }

    function renderChart(container, points, title, granularity, color) {
        container.replaceChildren();
        if (!points.length || points.every(point => point.count === 0)) {
            const empty = document.createElement("div");
            empty.className = "d-flex align-items-center justify-content-center text-muted small h-100";
            empty.textContent = "Chưa có dữ liệu trong khoảng thời gian này.";
            container.appendChild(empty);
            return;
        }

        const width = 420;
        const height = 260;
        const left = 42;
        const right = 10;
        const top = 16;
        const bottom = 46;
        const plotWidth = width - left - right;
        const plotHeight = height - top - bottom;
        const maxValue = Math.max(1, ...points.map(point => point.count));
        const tickStep = Math.max(1, Math.ceil(maxValue / 4));
        const scaleMaximum = tickStep * 4;
        const svg = document.createElementNS(svgNamespace, "svg");
        svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
        svg.setAttribute("role", "img");
        svg.setAttribute("aria-label", title);
        appendSvg(svg, "title", {}, title);

        for (let tick = 0; tick <= 4; tick += 1) {
            const value = scaleMaximum - tick * tickStep;
            const y = top + (plotHeight * tick) / 4;
            appendSvg(svg, "line", { x1: left, y1: y, x2: width - right, y2: y, class: "hrm-chart-grid" });
            appendSvg(svg, "text", { x: left - 8, y: y + 4, "text-anchor": "end", class: "hrm-chart-label" }, numberFormat.format(value));
        }

        const slotWidth = plotWidth / points.length;
        const barWidth = Math.min(24, slotWidth * 0.62);
        points.forEach((point, index) => {
            const barHeight = (point.count / scaleMaximum) * plotHeight;
            const x = left + index * slotWidth + (slotWidth - barWidth) / 2;
            const y = top + plotHeight - barHeight;
            const bar = appendSvg(svg, "rect", {
                x,
                y,
                width: barWidth,
                height: Math.max(point.count ? 1 : 0, barHeight),
                rx: 4,
                fill: color
            });
            appendSvg(bar, "title", {}, `${formatPeriod(point.periodStart, granularity)}: ${numberFormat.format(point.count)}`);
            appendSvg(svg, "text", {
                x: x + barWidth / 2,
                y: height - 16,
                "text-anchor": "middle",
                class: "hrm-chart-label"
            }, formatPeriod(point.periodStart, granularity));
        });

        container.appendChild(svg);
    }

    function renderOverduePositions(positions) {
        elements.overduePositions.replaceChildren();
        if (!positions.length) {
            const row = document.createElement("tr");
            const cell = document.createElement("td");
            cell.colSpan = 5;
            cell.className = "text-center text-muted py-4";
            cell.textContent = "Không có vị trí tuyển dụng trễ hạn.";
            row.appendChild(cell);
            elements.overduePositions.appendChild(row);
            return;
        }

        positions.forEach(position => {
            const row = document.createElement("tr");
            const titleCell = document.createElement("td");
            if (position.status === "PUBLISHED") {
                const link = document.createElement("a");
                link.href = `/Jobs/Detail/${encodeURIComponent(position.jobPostingId)}`;
                link.className = "fw-semibold text-decoration-none";
                link.textContent = position.title;
                titleCell.appendChild(link);
            } else {
                titleCell.textContent = position.title;
            }

            const departmentCell = document.createElement("td");
            departmentCell.textContent = position.departmentName || "—";
            const dateCell = document.createElement("td");
            dateCell.textContent = new Intl.DateTimeFormat("vi-VN").format(new Date(`${position.targetHireDate}T00:00:00`));
            const overdueCell = document.createElement("td");
            overdueCell.className = "text-end hrm-overdue-days";
            overdueCell.textContent = `${numberFormat.format(position.daysOverdue)} ngày`;
            const statusCell = document.createElement("td");
            const badge = document.createElement("span");
            badge.className = position.status === "EXPIRED" ? "badge bg-secondary-subtle text-secondary-emphasis" : "badge bg-success-subtle text-success-emphasis";
            badge.textContent = position.statusDisplay;
            statusCell.appendChild(badge);
            [titleCell, departmentCell, dateCell, overdueCell, statusCell].forEach(cell => row.appendChild(cell));
            elements.overduePositions.appendChild(row);
        });
    }

    function clearDashboard() {
        [elements.openPositions, elements.newApplications, elements.upcomingInterviews, elements.offersAwaitingResponse]
            .forEach(element => { element.textContent = "—"; });
        [elements.applicationsChart, elements.closedPositionsChart, elements.overduePositions]
            .forEach(element => element.replaceChildren());
        loadTime.textContent = "";
    }

    async function loadDashboard() {
        const startedAt = performance.now();
        error.classList.add("d-none");
        content.setAttribute("aria-busy", "true");
        retry.disabled = true;

        try {
            const response = await fetch(root.dataset.apiUrl, {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: { Accept: "application/json" }
            });
            if (!response.ok) throw new Error(`Dashboard request failed with status ${response.status}`);

            const dashboard = await response.json();
            elements.openPositions.textContent = numberFormat.format(dashboard.openPositions);
            elements.newApplications.textContent = numberFormat.format(dashboard.newApplicationsThisWeek);
            elements.upcomingInterviews.textContent = numberFormat.format(dashboard.upcomingInterviews);
            elements.offersAwaitingResponse.textContent = numberFormat.format(dashboard.offersAwaitingResponse);
            renderChart(elements.applicationsChart, dashboard.applicationsByWeek, "Số hồ sơ ứng tuyển theo tuần", "week", "#2563eb");
            renderChart(elements.closedPositionsChart, dashboard.closedPositionsByMonth, "Số vị trí đã đóng theo tháng", "month", "#059669");
            renderOverduePositions(dashboard.overduePositions);

            const clientDuration = performance.now() - startedAt;
            loadTime.textContent = `Truy vấn máy chủ ${numberFormat.format(dashboard.queryDurationMilliseconds)} ms · Tải và hiển thị ${numberFormat.format(clientDuration)} ms`;
        } catch (requestError) {
            clearDashboard();
            error.classList.remove("d-none");
            console.error("Unable to load the HR recruitment dashboard.", requestError);
        } finally {
            content.setAttribute("aria-busy", "false");
            retry.disabled = false;
        }
    }

    retry.addEventListener("click", loadDashboard);
    loadDashboard();
})();
