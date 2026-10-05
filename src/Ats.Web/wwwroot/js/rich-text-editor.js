/**
 * =========================================================
 * NoveraTech ATS — Rich Text Editor Engine (RULE.MD Compliant)
 * Hỗ trợ định dạng: Tiêu đề (H2, H3), In đậm, In nghiêng,
 * Bullet list, Numbered list, Xóa định dạng, Chèn mẫu gợi ý.
 * Không phụ thuộc thư viện CDN bên ngoài, tự động đồng bộ Form.
 * =========================================================
 */

(function (window, document) {
    'use strict';

    const Templates = {
        description: `<h2>1. Trách nhiệm & Nhiệm vụ chính</h2><ul><li>Trực tiếp tham gia thiết kế, phát triển và tối ưu hóa các module hệ thống phần mềm chịu tải cao.</li><li>Phối hợp cùng Product Owner, Tech Lead và nhóm kỹ thuật phân tích yêu cầu nghiệp vụ và kiến trúc.</li><li>Nghiên cứu áp dụng các công nghệ, thư viện mới nhằm nâng cao hiệu suất và trải nghiệm người dùng.</li><li>Thực hiện viết Unit Tests, đảm bảo mã nguồn an toàn bảo mật và tuân thủ chuẩn Clean Architecture.</li></ul><h2>2. Chỉ số đo lường hiệu quả (KPIs)</h2><ul><li>Hoàn thành 100% các cam kết mục tiêu Sprint đúng hạn và đạt chuẩn chất lượng.</li><li>Tỷ lệ bao phủ kiểm thử tự động (Unit Test Coverage) đạt tối thiểu 80%.</li><li>Thời gian xử lý lỗi (Defect Resolution Time) đáp ứng cam kết tiêu chuẩn SLA nội bộ.</li></ul>`,
        requirements: `<h2>1. Trình độ học vấn & Kinh nghiệm</h2><ul><li>Tốt nghiệp Đại học chuyên ngành Công nghệ thông tin, Khoa học Máy tính hoặc các khối ngành tương đương.</li><li>Tối thiểu từ 2 - 3 năm kinh nghiệm thực chiến phát triển ứng dụng trên nền tảng .NET / C# hoặc công nghệ tương đương.</li></ul><h2>2. Kỹ năng chuyên môn cốt lõi</h2><ul><li>Thành thạo C#, ASP.NET Core, Entity Framework Core và lập trình hướng đối tượng (OOP).</li><li>Có kinh nghiệm thiết kế RESTful APIs, Clean Architecture và tối ưu hiệu năng cơ sở dữ liệu PostgreSQL.</li><li>Quen thuộc với Git flow, Docker container, CI/CD và kiến trúc dịch vụ phân tán.</li></ul><h2>3. Kỹ năng mềm & Chứng chỉ ưu tiên</h2><ul><li>Khả năng đọc hiểu tài liệu kỹ thuật tiếng Anh tốt; tư duy giải quyết vấn đề mạch lạc và tinh thần trách nhiệm cao.</li><li>Kỹ năng giao tiếp và làm việc nhóm hiệu quả trong môi trường Agile/Scrum.</li><li>Ưu tiên ứng viên có chứng chỉ chuyên môn Cloud (AWS, Azure) hoặc chứng chỉ kỹ thuật quốc tế.</li></ul>`
    };

    function stripHtml(html) {
        if (!html) return '';
        const temp = document.createElement('div');
        temp.innerHTML = html;
        return (temp.textContent || temp.innerText || '').trim();
    }

    function countWordsAndChars(text) {
        const clean = text.trim();
        const chars = clean.length;
        const words = clean ? clean.split(/\s+/).filter(Boolean).length : 0;
        return { words, chars };
    }

    function initRichTextEditor(wrapper) {
        if (wrapper.dataset.richEditorInitialized === 'true') return;
        wrapper.dataset.richEditorInitialized = 'true';

        const content = wrapper.querySelector('.rich-editor-content');
        const hiddenInput = wrapper.querySelector('input[type="hidden"], textarea.rich-editor-target');
        const statsEl = wrapper.querySelector('.rich-editor-stats');
        const headingSelect = wrapper.querySelector('.rich-editor-heading-select');
        const templateBtn = wrapper.querySelector('.rich-editor-template-btn');

        if (!content) return;

        // 1. Nạp dữ liệu ban đầu từ hidden input (nếu có)
        if (hiddenInput && hiddenInput.value && !content.innerHTML.trim()) {
            content.innerHTML = hiddenInput.value;
        }

        // Cập nhật thống kê và input
        function syncContent() {
            let html = content.innerHTML;
            const plain = stripHtml(html);

            // Nếu chỉ chứa thẻ rỗng hoặc whitespace, chuẩn hóa về chuỗi rỗng
            if (!plain) {
                if (html.toLowerCase() === '<p><br></p>' || html.toLowerCase() === '<div><br></div>' || html.trim() === '<br>') {
                    content.innerHTML = '';
                    html = '';
                }
            }

            if (hiddenInput) {
                hiddenInput.value = html;
                // Kích hoạt event change cho validation
                hiddenInput.dispatchEvent(new Event('input', { bubbles: true }));
                hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
            }

            if (statsEl) {
                const { words, chars } = countWordsAndChars(plain);
                statsEl.textContent = `${words} từ | ${chars} ký tự`;
            }

            if (plain) {
                wrapper.classList.remove('is-invalid');
                const errSpan = wrapper.parentElement?.querySelector('.field-validation-error, .invalid-feedback');
                if (errSpan) errSpan.classList.add('d-none');
            }
        }

        // 2. Lắng nghe tương tác gõ văn bản
        content.addEventListener('input', syncContent);
        content.addEventListener('blur', syncContent);

        // 3. Xử lý các nút bấm trên thanh công cụ
        const buttons = wrapper.querySelectorAll('.rich-editor-btn[data-command]');
        buttons.forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                content.focus();
                const command = btn.getAttribute('data-command');
                const value = btn.getAttribute('data-value') || null;

                if (command) {
                    document.execCommand(command, false, value);
                    updateToolbarState();
                    syncContent();
                }
            });
        });

        // 4. Xử lý chọn Tiêu đề (Headings)
        if (headingSelect) {
            headingSelect.addEventListener('change', function () {
                content.focus();
                const val = headingSelect.value;
                if (val === 'h2') {
                    document.execCommand('formatBlock', false, '<h2>');
                } else if (val === 'h3') {
                    document.execCommand('formatBlock', false, '<h3>');
                } else {
                    document.execCommand('formatBlock', false, '<p>');
                }
                syncContent();
            });
        }

        // 5. Cập nhật trạng thái active cho nút toolbar theo vị trí con trỏ
        function updateToolbarState() {
            buttons.forEach(btn => {
                const cmd = btn.getAttribute('data-command');
                if (cmd && ['bold', 'italic', 'insertUnorderedList', 'insertOrderedList'].includes(cmd)) {
                    try {
                        if (document.queryCommandState(cmd)) {
                            btn.classList.add('is-active');
                        } else {
                            btn.classList.remove('is-active');
                        }
                    } catch (err) {
                        // ignore queryCommandState errors
                    }
                }
            });

            if (headingSelect) {
                try {
                    const block = document.queryCommandValue('formatBlock');
                    if (block === 'h2') headingSelect.value = 'h2';
                    else if (block === 'h3') headingSelect.value = 'h3';
                    else headingSelect.value = 'p';
                } catch (err) { }
            }
        }

        content.addEventListener('keyup', updateToolbarState);
        content.addEventListener('mouseup', updateToolbarState);

        // 6. Xử lý dán văn bản (Paste handler) làm sạch thẻ style rác
        content.addEventListener('paste', function (e) {
            e.preventDefault();
            const text = (e.originalEvent || e).clipboardData.getData('text/plain');
            document.execCommand('insertText', false, text);
            syncContent();
        });

        // 7. Xử lý nút Chèn Mẫu Gợi Ý (Quick Starter Template)
        if (templateBtn) {
            templateBtn.addEventListener('click', function (e) {
                e.preventDefault();
                const templateType = wrapper.getAttribute('data-template-type');
                const tplHtml = Templates[templateType];
                if (tplHtml) {
                    if (stripHtml(content.innerHTML).length > 0) {
                        if (!confirm('Nội dung hiện tại trong khung soạn thảo sẽ được thay thế bằng cấu trúc mẫu chuẩn. Bạn có chắc chắn muốn chèn không?')) {
                            return;
                        }
                    }
                    content.innerHTML = tplHtml;
                    syncContent();
                    content.focus();
                }
            });
        }

        // Đồng bộ thống kê ban đầu
        syncContent();
    }

    // Khởi tạo toàn cục cho tất cả editor trên trang
    function initAllEditors() {
        const wrappers = document.querySelectorAll('.rich-editor-wrapper');
        wrappers.forEach(initRichTextEditor);
    }

    // Tự động kích hoạt khi DOM tải xong
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAllEditors);
    } else {
        initAllEditors();
    }

    // Xuất khẩu API toàn cục phục vụ tương tác ngoài
    window.AtsRichEditor = {
        init: initAllEditors,
        initElement: initRichTextEditor,
        stripHtml: stripHtml,
        isEmpty: function (wrapperOrSelector) {
            const el = typeof wrapperOrSelector === 'string'
                ? document.querySelector(wrapperOrSelector)
                : wrapperOrSelector;
            if (!el) return true;
            const content = el.querySelector('.rich-editor-content');
            return !content || !stripHtml(content.innerHTML);
        },
        getContent: function (wrapperOrSelector) {
            const el = typeof wrapperOrSelector === 'string'
                ? document.querySelector(wrapperOrSelector)
                : wrapperOrSelector;
            if (!el) return '';
            const content = el.querySelector('.rich-editor-content');
            return content ? content.innerHTML : '';
        },
        setContent: function (wrapperOrSelector, html) {
            const el = typeof wrapperOrSelector === 'string'
                ? document.querySelector(wrapperOrSelector)
                : wrapperOrSelector;
            if (!el) return;
            const content = el.querySelector('.rich-editor-content');
            if (content) {
                content.innerHTML = html || '';
                content.dispatchEvent(new Event('input', { bubbles: true }));
            }
        }
    };

})(window, document);
