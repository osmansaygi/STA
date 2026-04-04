;;; INDISEKLE.lsp — Secilen TEXT/MTEXT iceriginin basina kullanici on eki ekler.
;;; Yukleme: APPLOAD veya (load "INDISEKLE.lsp")
;;; Komut: INDISEKLE

(defun c:INDISEKLE (/ ss prefix i e ed a1 old new n)
  (setq ss (ssget '((0 . "TEXT,MTEXT"))))
  (cond
    ((not ss)
     (princ "\nHic TEXT/MTEXT secilmedi.")
    )
    (T
     (setq prefix (getstring T "\nBasina eklenecek metin (indeks/on ek): "))
     (setq n 0
           i 0)
     (repeat (sslength ss)
       (setq e (ssname ss i)
             ed (entget e)
             a1 (assoc 1 ed))
       (if a1
         (progn
           (setq old (cdr a1)
                 new (strcat prefix old))
           (setq ed (subst (cons 1 new) a1 ed))
           (entmod ed)
           (entupd e)
           (setq n (1+ n))
         )
       )
       (setq i (1+ i))
     )
     (princ (strcat "\n" (itoa n) " yazi guncellendi."))
    )
  )
  (princ)
)

(princ "\nINDISEKLE yuklendi. Komut: INDISEKLE")
(princ)
